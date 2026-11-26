using System;
using System.Diagnostics;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The **station** departure split. A station plan ends on the hold state
    /// instead of a capture chain, so the planet rank-only cost has no entry
    /// state to price the brake against. <see cref="TripPlanner"/> enters at the
    /// hold state at the arrival date, drops the chain's duration, and requires
    /// no arrival band. This suite grids the cheap search against the full
    /// fast-probe station build on the routes that carry a climb, checks both
    /// no-climb cases, and pins the shipped station floor, anchors and arrival.
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
        /// The split now reaches a climbing station: <see cref="TripPlanner.Build"/>
        /// chooses the excess speed once per station build and hands it to every
        /// climb solve. At 60 days the chooser takes 2 km/s and the plan is
        /// cheaper than the unforced parabolic climb.
        /// </summary>
        [Test]
        public void AClimbingStationPlanUsesTheChosenSplit()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double seconds = 60.0 * SecondsPerDay;

            _planner.UseDepartureSplit = true;
            TrajectoryPlan withFlag = _planner.PlanAtDuration(ship,
                "ares_junction", seconds);
            double chosen = withFlag.DepartureVInfinityMps;
            _planner.UseDepartureSplit = false;
            TrajectoryPlan withoutFlag = _planner.PlanAtDuration(ship,
                "ares_junction", seconds);

            TestContext.WriteLine($"station split flag: with " +
                                  $"{withFlag.DeltaVMps / KmPerSecond:F4} km/s " +
                                  $"(v∞ {chosen / KmPerSecond:F3}), without " +
                                  $"{withoutFlag.DeltaVMps / KmPerSecond:F4} km/s");

            Assert.That(withFlag.IsFeasible, Is.True, withFlag.InfeasibleReason);
            Assert.That(withoutFlag.IsFeasible, Is.True, withoutFlag.InfeasibleReason);
            Assert.That(chosen, Is.GreaterThan(0.0),
                "a climbing station gets a chosen excess speed");
            Assert.That(withFlag.DeltaVMps, Is.LessThan(withoutFlag.DeltaVMps),
                "the split makes the station trip cheaper");
        }

        /// <summary>
        /// The station coast floor, before and after the split. From a Mars
        /// 300 km orbit to Gateway the floor is the shortest station trip the
        /// planner will certify; the split changes the departure weight it builds
        /// at every probe and at the certificate. The table is the measured
        /// before and after, and the after plan must be feasible and exact.
        /// </summary>
        [Test]
        public void TheStationCoastFloorIsSplitAware()
        {
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "mars", 300e3);

            _planner.UseDepartureSplit = false;
            TrajectoryPlan before = _planner.Plan(ship, "gateway");
            _planner.UseDepartureSplit = true;
            TrajectoryPlan after = _planner.Plan(ship, "gateway");

            TestContext.WriteLine("station floor | days | dv km/s | v∞ km/s | arcs | ok");
            WriteFloor("before", before);
            WriteFloor("after ", after);

            Assert.That(before.IsFeasible, Is.True, before.InfeasibleReason);
            Assert.That(after.IsFeasible, Is.True, after.InfeasibleReason);
            Assert.That(before.FlightTimeDays, Is.EqualTo(59.57357).Within(0.01),
                "the flag-off floor is the pre-split anchor");
            Assert.That(before.DeltaVMps / KmPerSecond,
                Is.EqualTo(76.4939).Within(0.001));
            Assert.That(after.FlightTimeDays, Is.EqualTo(54.36495).Within(0.01),
                "the split-aware floor is the new anchor");
            Assert.That(after.DeltaVMps / KmPerSecond,
                Is.EqualTo(98.4091).Within(0.001));
            Assert.That(after.DepartureVInfinityMps,
                Is.EqualTo(16.0 * KmPerSecond),
                "the shortest certified trip needs the grid's 16 km/s cap");
            Assert.That(after.Arcs.Count, Is.EqualTo(5));
            Assert.That(after.FlightTimeDays, Is.LessThan(before.FlightTimeDays),
                "the split-aware station floor is faster");
            AssertChainCloses(after);
            AssertArrivesOnTheHoldPoint(after, "gateway");
        }

        /// <summary>
        /// The fixed-duration station anchors, before and after: Gateway to Ares
        /// Junction at 60, 90, 120 and 160 days. The pre-split totals are the
        /// unforced parabolic climb; the split-aware totals are the chooser's. The
        /// table is the pinned re-baseline.
        /// </summary>
        [Test]
        public void TheStationAnchorsCarryTheChosenSplit()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            var anchors = new[]
            {
                (Days: 60.0, BeforeKm: 71.9303, AfterKm: 67.8154, VInfinityKms: 2.0),
                (Days: 90.0, BeforeKm: 39.3398, AfterKm: 38.9403, VInfinityKms: 1.0),
                (Days: 120.0, BeforeKm: 24.7331, AfterKm: 24.7331, VInfinityKms: 0.0),
                (Days: 160.0, BeforeKm: 15.1026, AfterKm: 15.0673, VInfinityKms: 0.5)
            };

            TestContext.WriteLine("gateway → ares_junction | d | before dv | before v∞ | " +
                                  "after dv | after v∞ | arcs");
            foreach ((double days, double beforeKm, double afterKm, double vInfinityKms)
                     in anchors)
            {
                double seconds = days * SecondsPerDay;
                _planner.UseDepartureSplit = false;
                TrajectoryPlan before = _planner.PlanAtDuration(ship,
                    "ares_junction", seconds);
                _planner.UseDepartureSplit = true;
                TrajectoryPlan after = _planner.PlanAtDuration(ship,
                    "ares_junction", seconds);

                TestContext.WriteLine($"{days,6:F0} | " +
                                      $"{before.DeltaVMps / KmPerSecond,10:F4} | " +
                                      $"{before.DepartureVInfinityMps / KmPerSecond,9:F3} | " +
                                      $"{after.DeltaVMps / KmPerSecond,9:F4} | " +
                                      $"{after.DepartureVInfinityMps / KmPerSecond,8:F3} | " +
                                      $"{after.Arcs.Count,4}");

                Assert.That(before.IsFeasible, Is.True, before.InfeasibleReason);
                Assert.That(after.IsFeasible, Is.True, after.InfeasibleReason);
                Assert.That(Math.Round(before.DeltaVMps / KmPerSecond, 4),
                    Is.EqualTo(beforeKm), $"{days:F0} d flag-off anchor");
                Assert.That(Math.Round(after.DeltaVMps / KmPerSecond, 4),
                    Is.EqualTo(afterKm), $"{days:F0} d split-aware anchor");
                Assert.That(after.DepartureVInfinityMps,
                    Is.EqualTo(vInfinityKms * KmPerSecond),
                    $"{days:F0} d chosen excess speed");
                Assert.That(after.Arcs.Count, Is.EqualTo(5));
                AssertChainCloses(after);
                AssertArrivesOnTheHoldPoint(after, "ares_junction");
            }
        }

        /// <summary>
        /// The Mars orbit to Gateway anchor at 120 days: the unforced plan is
        /// 25.648 km/s; the split-aware plan is the chooser's. The plan arrives on
        /// the hold point, and the chooser's own search is inside its 50 ms
        /// budget.
        /// </summary>
        [Test]
        public void TheMarsOrbitAnchorAndTheSearchBudget()
        {
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "mars", 300e3);
            double seconds = 120.0 * SecondsPerDay;

            _planner.UseDepartureSplit = false;
            TrajectoryPlan before = _planner.PlanAtDuration(ship, "gateway", seconds);
            _planner.UseDepartureSplit = true;
            TrajectoryPlan after = _planner.PlanAtDuration(ship, "gateway", seconds);
            double searchSeconds = _planner.LastDepartureSplitSeconds;

            TestContext.WriteLine($"mars 300 km → gateway 120 d: before " +
                                  $"{before.DeltaVMps / KmPerSecond:F4} km/s " +
                                  $"(v∞ {before.DepartureVInfinityMps / KmPerSecond:F3}), " +
                                  $"after {after.DeltaVMps / KmPerSecond:F4} km/s " +
                                  $"(v∞ {after.DepartureVInfinityMps / KmPerSecond:F3}), " +
                                  $"search {searchSeconds * 1000.0:F1} ms");

            Assert.That(before.DeltaVMps / KmPerSecond,
                Is.EqualTo(25.648).Within(0.001), "the flag-off anchor is unmoved");
            Assert.That(after.IsFeasible, Is.True, after.InfeasibleReason);
            Assert.That(Math.Round(after.DeltaVMps / KmPerSecond, 4),
                Is.EqualTo(24.3137), "the split-aware anchor");
            Assert.That(after.DepartureVInfinityMps, Is.EqualTo(1.0 * KmPerSecond),
                "the chooser takes 1 km/s at 120 d");
            Assert.That(searchSeconds, Is.LessThan(0.050),
                "the station chooser is inside its 50 ms budget");
            AssertChainCloses(after);
            AssertArrivesOnTheHoldPoint(after, "gateway");
        }

        /// <summary>
        /// The planet anchor and the heliocentric no-climb control do not move.
        /// Gateway to Mars at 90 days stays 38.5411 km/s, and Ares Junction to
        /// Gateway at 160 days stays 12.2391 km/s.
        /// </summary>
        [Test]
        public void ThePlanetAndHeliocentricAnchorsDoNotMove()
        {
            _planner.UseDepartureSplit = true;

            TrajectoryPlan planet = _planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars", 90.0 * SecondsPerDay);
            TrajectoryPlan heliocentric = _planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "ares_junction"), "gateway",
                160.0 * SecondsPerDay);

            TestContext.WriteLine($"gateway → mars 90 d: " +
                                  $"{planet.DeltaVMps / KmPerSecond:F4} km/s; " +
                                  $"ares_junction → gateway 160 d: " +
                                  $"{heliocentric.DeltaVMps / KmPerSecond:F4} km/s");

            Assert.That(Math.Round(planet.DeltaVMps / KmPerSecond, 4),
                Is.EqualTo(38.5411), "the planet split arithmetic is unchanged");
            Assert.That(Math.Round(heliocentric.DeltaVMps / KmPerSecond, 4),
                Is.EqualTo(12.2391), "a heliocentric station start has no climb");
            Assert.That(heliocentric.DepartureVInfinityMps, Is.EqualTo(-1.0));
        }

        private static void WriteFloor(string label, TrajectoryPlan plan)
        {
            TestContext.WriteLine($"{label} | {plan.FlightTimeDays,9:F5} | " +
                                  $"{plan.DeltaVMps / KmPerSecond,9:F4} | " +
                                  $"{plan.DepartureVInfinityMps / KmPerSecond,7:F3} | " +
                                  $"{plan.Arcs.Count,4} | " +
                                  $"{(plan.IsFeasible ? "ok" : plan.InfeasibleReason)}");
        }

        /// <summary>The profile's chain gate: every seam inside 1 km and 1 m/s.</summary>
        private static void AssertChainCloses(TrajectoryPlan plan)
        {
            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];
                double positionStep = Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0));
                double velocityStep = (left.Path.VelocityAt(1.0)
                                       + right.EntryImpulseVectorMps
                                       - right.Path.VelocityAt(0.0)).Magnitude;

                Assert.That(positionStep, Is.LessThan(1e3),
                    $"seam {i} ({left.Phase}->{right.Phase}) position");
                Assert.That(velocityStep, Is.LessThan(1.0),
                    $"seam {i} ({left.Phase}->{right.Phase}) velocity");
            }
        }

        /// <summary>The station arrival: the last arc ends on the hold state at
        /// the requested date, inside 1 m and 1e-3 m/s.</summary>
        private void AssertArrivesOnTheHoldPoint(TrajectoryPlan plan, string stationId)
        {
            BodyRuntime station = _model.Get(stationId);
            _model.ComputeState(station, plan.ArrivalJulianDate,
                out Vector3d stationPosition, out Vector3d stationVelocity);
            Vector3d hold = stationPosition
                            - stationVelocity.Normalized * TripPlanner.StationHoldDistanceM;

            PlanArc last = plan.Arcs[plan.Arcs.Count - 1];
            double positionStep = Vector3d.Distance(last.Path.PositionAt(1.0), hold);
            double velocityStep = (last.Path.VelocityAt(1.0) - stationVelocity).Magnitude;
            TestContext.WriteLine($"arrival: {positionStep:E3} m, " +
                                  $"{velocityStep:E3} m/s from the hold state");

            Assert.That(positionStep, Is.LessThan(1.0),
                "the velocity match ends on the hold point");
            Assert.That(velocityStep, Is.LessThan(1e-3),
                "the velocity match ends on the station's velocity");
            Assert.That(plan.ArrivalJulianDate,
                Is.EqualTo(plan.DepartureJulianDate + plan.FlightTimeSeconds
                    / SecondsPerDay).Within(1e-6));
        }

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
