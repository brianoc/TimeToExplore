using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The departure climb split. Step 1 measured the trade between the escape
    /// conic's hyperbolic excess speed and the heliocentric coast at a fixed
    /// total trip time, using the test-only
    /// <see cref="TripPlanner.DepartureVInfinityOverrideMps"/> seam; step 2 adds
    /// the search behind the internal <see cref="TripPlanner.UseDepartureSplit"/>
    /// flag, which is off by default so every returned plan is unchanged. The
    /// measurement tests keep the seam; the choice tests turn the flag on and
    /// check the chosen plan and the flag-off anchors.
    /// </summary>
    [Category("flight")]
    public class DepartureClimbSplitTests
    {
        private const string TargetId = "mars";
        private const double BurnLevelG = 0.3;
        private const double SecondsPerDay = JulianDate.SecondsPerDay;

        /// <summary>The seam's own units: v∞ in m/s, not km/s.</summary>
        private const double KmPerSecond = 1_000.0;

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
            _planner.UseDepartureSplit = false;
            _planner.Trace = null;
        }

        /// <summary>
        /// The split sweep: four durations and four excess speeds, paired where
        /// the forced climb fits the requested time. Each plan is exact, feasible
        /// and on the target parking orbit; the table is the measured trade.
        /// </summary>
        [Test]
        public void TheForcedExcessSpeedMovesTheCostBetweenTheClimbAndTheCoast()
        {
            var sweep = new[]
            {
                new SweepCase(8.0, 10.0),
                new SweepCase(18.0, 0.0),
                new SweepCase(18.0, 1.0),
                new SweepCase(18.0, 3.0),
                new SweepCase(18.0, 10.0),
                new SweepCase(30.0, 0.0),
                new SweepCase(30.0, 3.0),
                new SweepCase(30.0, 10.0),
                new SweepCase(90.0, 0.0),
                new SweepCase(90.0, 1.0),
                new SweepCase(90.0, 3.0),
                new SweepCase(90.0, 10.0)
            };

            TestContext.WriteLine("total d | v∞ km/s | climb d | break dv | depart dv | " +
                                  "brake dv | total dv | propellant t | worst seam");
            var plans = new TrajectoryPlan[sweep.Length];
            TrajectoryPlan current = null;
            TrajectoryPlan fast = null;
            TrajectoryPlan longTrip = null;
            for (int i = 0; i < sweep.Length; i++)
            {
                SweepCase item = sweep[i];
                plans[i] = PlanAt(item.Days, item.VInfinityKms);
                PrintRow(item, plans[i]);

                if (item.Days == 18.0 && item.VInfinityKms == 0.0) current = plans[i];
                if (item.Days == 18.0 && item.VInfinityKms == 10.0) fast = plans[i];
                if (item.Days == 90.0 && item.VInfinityKms == 1.0) longTrip = plans[i];
            }

            for (int i = 0; i < sweep.Length; i++)
            {
                SweepCase item = sweep[i];
                TrajectoryPlan plan = plans[i];
                string label = $"{item.Days:F0} d, v∞ {item.VInfinityKms:F0} km/s";

                Assert.That(plan.IsFeasible, Is.True, $"{label}: {plan.InfeasibleReason}");
                Assert.That(plan.FlightTimeDays, Is.EqualTo(item.Days).Within(1e-4),
                    label);
                AssertArrivesOnTheParkingOrbit(plan, label);
                AssertChainCloses(plan, label);
            }

            // The measured anchors. Tolerances are wide on purpose: they pin the
            // trade's shape, not the last digit, and step 3 will re-baseline them
            // when the split is chosen automatically.
            TestContext.WriteLine($"18 d current plan: " +
                                  $"{current.DeltaVMps / KmPerSecond:F1} km/s");
            Assert.That(current.DeltaVMps / KmPerSecond, Is.EqualTo(706.1).Within(5.0),
                "the 18-day trip still takes the minimum-energy 10.74-day climb");

            TestContext.WriteLine($"18 d v∞ 10: {fast.DeltaVMps / KmPerSecond:F1} km/s");
            Assert.That(fast.DeltaVMps, Is.LessThan(300.0 * KmPerSecond),
                "a fast climb frees the coast and cuts the 18-day total below 300 km/s");

            TestContext.WriteLine($"90 d v∞ 1: " +
                                  $"{longTrip.DeltaVMps / KmPerSecond:F2} km/s");
            Assert.That(longTrip.DeltaVMps / KmPerSecond, Is.EqualTo(38.54).Within(0.2),
                "at 90 days the optimum is about 1 km/s");
            Assert.That(longTrip.DeltaVMps, Is.LessThan(38.7 * KmPerSecond));
        }

        /// <summary>
        /// The fast end of the split: with a 10 km/s excess the climb is about a
        /// day, so the total trip can be much shorter than the 10.7-day climb.
        /// This prints the shortest feasible duration in the 5–10-day probe and
        /// asserts nothing about the floor; step 3 re-sweeps and certifies it.
        /// </summary>
        [Test]
        public void AHighExcessSpeedLetsTheTripBuildBelowTheParabolicClimb()
        {
            const double vInfinityKms = 10.0;
            double shortest = double.PositiveInfinity;

            TestContext.WriteLine("floor probe at v∞ 10 km/s:");
            for (double days = 5.0; days <= 10.0 + 1e-9; days += 1.0)
            {
                TrajectoryPlan plan = PlanAt(days, vInfinityKms);
                TestContext.WriteLine($"  {days,4:F0} d: " +
                                      $"{(plan.IsFeasible ? "feasible" : "refused")} " +
                                      $"{plan.FlightTimeDays:F5} d, " +
                                      $"{plan.DeltaVMps / KmPerSecond:F1} km/s" +
                                      (plan.IsFeasible ? "" : $", {plan.InfeasibleReason}"));

                if (!plan.IsFeasible) continue;

                Assert.That(plan.FlightTimeDays, Is.EqualTo(days).Within(1e-4),
                    $"{days:F0} d");
                AssertArrivesOnTheParkingOrbit(plan, $"{days:F0} d");
                AssertChainCloses(plan, $"{days:F0} d");
                if (plan.FlightTimeDays < shortest) shortest = plan.FlightTimeDays;
            }

            TestContext.WriteLine($"shortest feasible: {shortest:F2} d");
            Assert.That(double.IsFinite(shortest), Is.True,
                "the probe must find at least the 10-day plan");
        }

        /// <summary>
        /// The split search at three durations. The choice is the escape conic's
        /// excess speed that minimises a fast probe build's total delta-v plus the
        /// break; the plan it then builds must be exact, on the Mars parking
        /// orbit, and cheaper than the step-1 parabolic climb by the measured
        /// margin. The candidate grid and its costs are printed through the
        /// planner's trace.
        /// </summary>
        [Test]
        public void TheSearchChoosesTheSplitThatMinimisesTheTotalAtEachDuration()
        {
            _planner.UseDepartureSplit = true;
            _planner.Trace = line => TestContext.WriteLine(line);

            var durations = new[] { 18.0, 30.0, 90.0 };
            var limits = new[] { 300.0, 160.0, 38.7 };
            var plans = new TrajectoryPlan[durations.Length];

            for (int i = 0; i < durations.Length; i++)
            {
                double days = durations[i];
                plans[i] = PlanWithSplit(days);

                TrajectoryPlan plan = plans[i];
                PlanArc escape = FindArc(plan, FlightPhase.Escape, false);
                TestContext.WriteLine($"split at {days:F0} d: chose " +
                                      $"{plan.DepartureVInfinityMps / KmPerSecond:F3} km/s, " +
                                      $"{plan.FlightTimeDays:F5} d, " +
                                      $"{plan.DeltaVMps / KmPerSecond:F3} km/s, " +
                                      $"{plan.PropellantKg / 1_000.0:F1} t, escape " +
                                      $"{(escape == null ? 0.0 : escape.DurationSeconds / SecondsPerDay):F4} d, " +
                                      $"search {_planner.LastDepartureSplitSeconds * 1_000.0:F0} ms");

                Assert.That(plan.IsFeasible, Is.True, $"{days:F0} d: {plan.InfeasibleReason}");
                Assert.That(plan.FlightTimeDays, Is.EqualTo(days).Within(1e-4),
                    $"{days:F0} d");
                Assert.That(plan.DeltaVMps, Is.LessThan(limits[i] * KmPerSecond),
                    $"{days:F0} d total");
                AssertArrivesOnTheParkingOrbit(plan, $"{days:F0} d split");
                AssertChainCloses(plan, $"{days:F0} d split");

                Assert.That(escape, Is.Not.Null, $"{days:F0} d: an escape arc");
                if (days == 18.0)
                {
                    Assert.That(escape.DurationSeconds / SecondsPerDay, Is.LessThan(5.0),
                        "18 d: the escape is the chosen fast climb, not the " +
                        "10.74066-day parabolic one");
                }
                Assert.That(InferVInfinityMps(escape),
                    Is.EqualTo(plan.DepartureVInfinityMps).Within(1.0),
                    $"{days:F0} d: the plan reports the excess its conic carries");
            }

            // The step-1 measured optimum at 18 days is 10 km/s; the search is
            // allowed to land within a local refinement of it, not on it.
            Assert.That(plans[0].DepartureVInfinityMps,
                Is.EqualTo(10.0 * KmPerSecond).Within(4.0 * KmPerSecond),
                "18 d chooses a fast climb near the measured optimum");
        }

        /// <summary>
        /// The flag off restores step 1 exactly: the parabolic climb fits, so the
        /// planner takes it, and the two measured anchors are unchanged. This is
        /// the promise the flag makes while the split is being wired in.
        /// </summary>
        [Test]
        public void TheFlagOffKeepsTheStepOneAnchors()
        {
            TrajectoryPlan eighteen = PlanWithoutSplit(18.0);
            TrajectoryPlan ninety = PlanWithoutSplit(90.0);

            TestContext.WriteLine($"flag off: 18 d " +
                                  $"{eighteen.DeltaVMps / KmPerSecond:F4} km/s, 90 d " +
                                  $"{ninety.DeltaVMps / KmPerSecond:F4} km/s");

            Assert.That(eighteen.IsFeasible, Is.True, eighteen.InfeasibleReason);
            Assert.That(eighteen.DeltaVMps / KmPerSecond,
                Is.EqualTo(706.091).Within(5.0),
                "the 18-day anchor is the step-1 plan");
            Assert.That(eighteen.DepartureVInfinityMps, Is.EqualTo(0.0),
                "the parabolic climb is the minimum-energy one");
            PlanArc escape = FindArc(eighteen, FlightPhase.Escape, false);
            Assert.That(escape, Is.Not.Null);
            Assert.That(escape.DurationSeconds / SecondsPerDay,
                Is.EqualTo(10.74066).Within(0.01),
                "the 18-day climb is still the parabolic one");

            Assert.That(ninety.IsFeasible, Is.True, ninety.InfeasibleReason);
            Assert.That(ninety.DeltaVMps / KmPerSecond,
                Is.EqualTo(38.968).Within(0.2),
                "the 90-day anchor is the step-1 plan");
            Assert.That(ninety.DepartureVInfinityMps, Is.EqualTo(0.0));
        }

        /// <summary>
        /// A ship outside every sphere has no departure body, no break, no escape
        /// conic and no split to choose: the flag cannot change its plan. Ares
        /// Junction → Gateway at 160 days is the measured three-arc station trip.
        /// </summary>
        [Test]
        public void AHeliocentricStartIsUnchangedByTheFlag()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "ares_junction");
            double duration = 160.0 * SecondsPerDay;

            _planner.UseDepartureSplit = true;
            TrajectoryPlan withSplit = _planner.PlanAtDuration(ship, "gateway", duration);

            _planner.UseDepartureSplit = false;
            TrajectoryPlan withoutSplit = _planner.PlanAtDuration(ship, "gateway", duration);

            TestContext.WriteLine($"heliocentric start: " +
                                  $"{withSplit.Arcs.Count} arcs, " +
                                  $"{withSplit.DeltaVMps / KmPerSecond:F4} km/s both ways");

            Assert.That(withSplit.IsFeasible, Is.True, withSplit.InfeasibleReason);
            Assert.That(FindArc(withSplit, FlightPhase.Escape, false), Is.Null,
                "there is no departure conic to split");
            Assert.That(withSplit.DepartureVInfinityMps, Is.EqualTo(-1.0),
                "no split is chosen");
            Assert.That(withSplit.DeltaVMps,
                Is.EqualTo(withoutSplit.DeltaVMps),
                "the flag does not change a heliocentric plan");
            Assert.That(withSplit.FlightTimeDays,
                Is.EqualTo(withoutSplit.FlightTimeDays));
        }

        /// <summary>
        /// The search is deterministic: the same state, duration, level and style
        /// choose the same excess speed and return the same plan. It is
        /// per-instance state on the planner, never a static.
        /// </summary>
        [Test]
        public void TwoBuildsChooseTheSameSplitAndReturnTheSamePlan()
        {
            _planner.UseDepartureSplit = true;

            TrajectoryPlan first = PlanWithSplit(18.0);
            TrajectoryPlan second = PlanWithSplit(18.0);

            Assert.That(second.DepartureVInfinityMps,
                Is.EqualTo(first.DepartureVInfinityMps),
                "the same request chooses the same excess speed");
            Assert.That(second.DeltaVMps, Is.EqualTo(first.DeltaVMps));
            Assert.That(second.FlightTimeDays, Is.EqualTo(first.FlightTimeDays));
            Assert.That(second.Arcs.Count, Is.EqualTo(first.Arcs.Count));
        }

        private TrajectoryPlan PlanAt(double days, double vInfinityKms)
        {
            _planner.DepartureVInfinityOverrideMps = vInfinityKms * KmPerSecond;
            return _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"),
                TargetId, days * SecondsPerDay);
        }

        /// <summary>One Gateway → Mars build with the split search on.</summary>
        private TrajectoryPlan PlanWithSplit(double days)
        {
            _planner.DepartureVInfinityOverrideMps = -1.0;
            _planner.UseDepartureSplit = true;
            return _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"),
                TargetId, days * SecondsPerDay);
        }

        /// <summary>One Gateway → Mars build with the split search off: the
        /// profile before this flag existed.</summary>
        private TrajectoryPlan PlanWithoutSplit(double days)
        {
            _planner.DepartureVInfinityOverrideMps = -1.0;
            _planner.UseDepartureSplit = false;
            return _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"),
                TargetId, days * SecondsPerDay);
        }

        /// <summary>The excess speed a plan's escape conic actually carries,
        /// from its own start state: <c>v² − 2μ/r</c> at the parking radius. It
        /// is an independent read of the split, not the planner's own
        /// bookkeeping.</summary>
        private double InferVInfinityMps(PlanArc escape)
        {
            BodyRuntime body = _model.Get(escape.ReferenceBodyId);
            Assert.That(body, Is.Not.Null, "the escape arc names its departure body");

            _model.ComputeState(body, escape.StartJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            double radius = (escape.Path.PositionAt(0.0) - bodyPosition).Magnitude;
            double speed = (escape.Path.VelocityAt(0.0) - bodyVelocity).Magnitude;
            double excessSquared = speed * speed - 2.0 * body.Gm / radius;
            return excessSquared > 0.0 ? Math.Sqrt(excessSquared) : 0.0;
        }

        private static PlanArc FindArc(TrajectoryPlan plan, FlightPhase phase,
            bool finiteBurn)
        {
            foreach (PlanArc arc in plan.Arcs)
                if (arc.Phase == phase && arc.IsFiniteBurn == finiteBurn) return arc;

            return null;
        }

        private static void PrintRow(SweepCase item, TrajectoryPlan plan)
        {
            PlanArc escape = FindArc(plan, FlightPhase.Escape, false);
            PlanArc breakArc = FindArc(plan, FlightPhase.Departure, false);
            PlanArc departure = FindArc(plan, FlightPhase.Cruise, true);
            PlanArc brake = FindArc(plan, FlightPhase.Capture, true);
            double drive = plan.FiniteBurnAccelerationMps2;

            var seam = WorstSeam(plan);
            TestContext.WriteLine(
                $"{plan.FlightTimeDays,7:F4} | {item.VInfinityKms,7:F1} | " +
                $"{(escape == null ? 0.0 : escape.DurationSeconds / SecondsPerDay),7:F4} | " +
                $"{(breakArc == null ? 0.0 : breakArc.DeltaVMps) / KmPerSecond,8:F3} | " +
                $"{(departure == null ? 0.0 : departure.DeltaVMps) / KmPerSecond,9:F3} | " +
                $"{(brake == null ? 0.0 : brake.DeltaVMps) / KmPerSecond,8:F3} | " +
                $"{plan.DeltaVMps / KmPerSecond,8:F3} | " +
                $"{plan.PropellantKg / 1_000.0,8:F1} | " +
                $"{seam.PositionM:F1} m / {seam.VelocityMps:F3} m/s");
            TestContext.WriteLine($"    peak {plan.PeakThrustMps2:F4} m/s², " +
                                  $"drive {drive:F4} m/s², arcs {plan.Arcs.Count}");
        }

        /// <summary>
        /// The worst seam in the chain: the same measure the profile's 1 km and
        /// 1 m/s gate uses. Velocity includes the right arc's entry impulse, so a
        /// conic capture's periapsis burns are not counted as discontinuities.
        /// </summary>
        private static (double PositionM, double VelocityMps) WorstSeam(
            TrajectoryPlan plan)
        {
            double worstPosition = 0.0;
            double worstVelocity = 0.0;
            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];
                double positionStep = Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0));
                double velocityStep = (left.Path.VelocityAt(1.0)
                                       + right.EntryImpulseVectorMps
                                       - right.Path.VelocityAt(0.0)).Magnitude;
                if (positionStep > worstPosition) worstPosition = positionStep;
                if (velocityStep > worstVelocity) worstVelocity = velocityStep;
            }

            return (worstPosition, worstVelocity);
        }

        private static void AssertChainCloses(TrajectoryPlan plan, string label)
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

                Assert.That(positionStep, Is.LessThan(1_000.0),
                    $"{label}: seam {i} ({left.Phase}->{right.Phase}) position");
                Assert.That(velocityStep, Is.LessThan(1.0),
                    $"{label}: seam {i} ({left.Phase}->{right.Phase}) velocity");
            }

            Assert.That(plan.Arcs[plan.Arcs.Count - 1].EndJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-12), label);
        }

        private void AssertArrivesOnTheParkingOrbit(TrajectoryPlan plan, string label)
        {
            BodyRuntime target = _model.Get(TargetId);
            _model.ComputeState(target, plan.ArrivalJulianDate,
                out Vector3d targetPosition, out Vector3d targetVelocity);

            double radius = (plan.ArrivalState.PositionM - targetPosition).Magnitude;
            double speed = (plan.ArrivalState.VelocityMps - targetVelocity).Magnitude;
            double expectedRadius = ParkingOrbit.ConicRadiusM(target);
            double expectedSpeed = TwoBody.CircularSpeedMps(target.Gm, expectedRadius);

            Assert.That(radius, Is.EqualTo(expectedRadius).Within(1.0),
                $"{label}: arrival radius");
            Assert.That(speed, Is.EqualTo(expectedSpeed).Within(0.01),
                $"{label}: arrival speed");
        }

        private readonly struct SweepCase
        {
            public SweepCase(double days, double vInfinityKms)
            {
                Days = days;
                VInfinityKms = vInfinityKms;
            }

            public double Days { get; }
            public double VInfinityKms { get; }
        }
    }
}
