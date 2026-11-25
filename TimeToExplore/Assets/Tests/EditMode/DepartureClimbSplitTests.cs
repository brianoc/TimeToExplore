using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Step 1 of the departure climb split: measures the trade between the
    /// escape conic's hyperbolic excess speed and the heliocentric coast at a
    /// fixed total trip time. Every plan is the real planner; the only extra is
    /// the test-only <see cref="TripPlanner.DepartureVInfinityOverrideMps"/>
    /// seam, which forces one v∞ instead of solving the minimum that fits. With
    /// the seam unset every plan is identical to today's. Nothing here chooses a
    /// split: these are the measurements step 2's search must match.
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

        private TrajectoryPlan PlanAt(double days, double vInfinityKms)
        {
            _planner.DepartureVInfinityOverrideMps = vInfinityKms * KmPerSecond;
            return _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"),
                TargetId, days * SecondsPerDay);
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
