using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Where one route build's time goes, measured rather than guessed. The two
    /// cases are a slider retime (one full-field <see cref="TripPlanner.PlanAtDuration"/>
    /// at a mid-window duration) and a first plot (<see cref="TripPlanner.PlanFastest"/>,
    /// the coast floor plus the burn-only moving target) at the epoch from
    /// Gateway, Mars, 0.3 g, conic capture.
    ///
    /// The test prints the split and asserts only structure: the counts, the
    /// phases belonging to the call, and the current Mars floor. It never asserts
    /// a time, because these are editor/Mono numbers and the run-to-run spread is
    /// larger than any difference the assertions could police.
    ///
    /// Warm-up rule: one untimed call per case, then three measured runs; the
    /// wall time and allocation reported are the median of the three. The planner
    /// and its gravity field are shared by the warm-up and the runs, so the
    /// one-time field setup is not charged to the measured builds. Allocation
    /// comes from Unity's "GC Allocated In Frame" profiler counter because Mono
    /// returns zero from <see cref="GC.GetAllocatedBytesForCurrentThread"/> in
    /// this editor.
    /// </summary>
    [Category("deep")]
    public class RouteBuildMetricsTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TripPlanner _planner;
        private ProfilerRecorder _gcAllocated;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _planner = new TripPlanner(_model, _gravity)
            {
                BurnLevelG = 0.3,
                ArrivalStyle = CaptureStyle.Conic
            };
            _gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory,
                "GC Allocated In Frame");
        }

        [TearDown]
        public void TearDown() => _gcAllocated.Dispose();

        private readonly struct Run
        {
            public Run(double wallSeconds, Allocated allocated, PlanMetrics metrics,
                TrajectoryPlan plan)
            {
                WallSeconds = wallSeconds;
                Allocated = allocated;
                Metrics = metrics;
                Plan = plan;
            }

            public double WallSeconds { get; }
            public Allocated Allocated { get; }
            public PlanMetrics Metrics { get; }
            public TrajectoryPlan Plan { get; }
        }

        private readonly struct Allocated
        {
            public Allocated(long perThread, long recorder, long totalMemory)
            {
                PerThread = perThread;
                Recorder = recorder;
                TotalMemory = totalMemory;
            }

            /// <summary>Managed bytes allocated by this thread in the call, or
            /// zero/-1 when the runtime does not report it.</summary>
            public long PerThread { get; }

            /// <summary>Bytes the Unity profiler's "GC Allocated In Frame"
            /// counter advanced by during the call, or -1 when the recorder is
            /// not available.</summary>
            public long Recorder { get; }

            /// <summary>Growth of Unity's total allocated memory over the call.
            /// Retained bytes, not allocation churn, and the editor's own work
            /// is included: approximate.</summary>
            public long TotalMemory { get; }

            /// <summary>The usable count, preferring the exact per-thread
            /// counter, then the profiler recorder, then the retained-memory
            /// delta.</summary>
            public long Bytes => PerThread > 0 ? PerThread
                : Recorder > 0 ? Recorder
                : TotalMemory > 0 ? TotalMemory : 0;

            public string Source => PerThread > 0
                ? "GC.GetAllocatedBytesForCurrentThread"
                : Recorder > 0 ? "Profiler GC Allocated In Frame"
                : TotalMemory > 0
                    ? "Profiler.GetTotalAllocatedMemoryLong (retained, approximate)"
                    : "unavailable";
        }

        private Run[] Measure(int count, Func<PlanMetrics, TrajectoryPlan> build)
        {
            var runs = new Run[count];
            for (int i = 0; i < count; i++)
            {
                var metrics = new PlanMetrics();
                _planner.Metrics = metrics;
                metrics.Reset();
                long perThreadBefore = PerThreadAllocatedBytes();
                long recorderBefore = GcAllocatedInFrame();
                long totalMemoryBefore = TotalAllocatedMemory();
                long started = Stopwatch.GetTimestamp();
                TrajectoryPlan plan = build(metrics);
                double seconds = (Stopwatch.GetTimestamp() - started)
                                 / (double)Stopwatch.Frequency;
                Allocated allocated = new Allocated(
                    Difference(perThreadBefore, PerThreadAllocatedBytes()),
                    Difference(recorderBefore, GcAllocatedInFrame()),
                    Difference(totalMemoryBefore, TotalAllocatedMemory()));
                runs[i] = new Run(seconds, allocated, metrics, plan);
            }

            _planner.Metrics = null;
            return runs;
        }

        private static long Difference(long before, long after) =>
            before >= 0 && after >= before ? after - before : -1;

        private double TimeUninstrumented(Action build)
        {
            long started = Stopwatch.GetTimestamp();
            build();
            return (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        }

        private static long PerThreadAllocatedBytes()
        {
            // Mono on some platforms does not implement the per-thread counter
            // and returns zero instead of throwing; the caller treats zero as
            // unavailable.
            try
            {
                return GC.GetAllocatedBytesForCurrentThread();
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>The Unity profiler's per-frame managed-allocation counter.
        /// Reading it before and after a synchronous call gives the bytes that
        /// call allocated, provided the editor does not advance a frame in the
        /// middle. Returns -1 when the recorder is not available.</summary>
        private long GcAllocatedInFrame() =>
            _gcAllocated.Valid ? _gcAllocated.CurrentValue : -1;

        private static long TotalAllocatedMemory()
        {
            try
            {
                return Profiler.GetTotalAllocatedMemoryLong();
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static Run Median(Run[] runs) =>
            runs.OrderBy(run => run.WallSeconds).ElementAt(runs.Length / 2);

        private static void WriteRuns(string label, Run[] runs)
        {
            TestContext.WriteLine($"{label} runs: " + string.Join(", ",
                runs.Select(run => $"{run.WallSeconds * 1000.0:F1} ms")));
        }

        private static string Phase(double seconds, double wallSeconds)
        {
            double share = wallSeconds > 0.0 ? seconds / wallSeconds : 0.0;
            return $"{seconds * 1000.0:F1} ms ({share:P1} of wall)";
        }

        [Test]
        public void ARetimeAndAFirstPlotSplitIntoPinPassesReportedArcsAndTheBrake()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TestContext.WriteLine("Editor/Mono measurement at " +
                                  $"JD {ship.JulianDate:F1}, Gateway, Mars 0.3 g, " +
                                  "conic capture.");
            TestContext.WriteLine($"allocation counters: per-thread " +
                                  $"{GC.GetAllocatedBytesForCurrentThread()}, " +
                                  $"recorder valid {_gcAllocated.Valid}, " +
                                  $"current {_gcAllocated.CurrentValue}, " +
                                  $"last {_gcAllocated.LastValue}");

            // ---- one retime: a mid-window duration ----
            // The live Mars window runs from the 6.66965 d full-blast minimum to
            // 21.5025 d, three times the 7.16751 d coast floor, so 14.0 d is
            // inside it and needs no short coast.
            const double retimeDays = 14.0;
            double retimeSeconds = retimeDays * JulianDate.SecondsPerDay;
            _planner.PlanAtDuration(ship, "mars", retimeSeconds); // warm-up
            Run[] retimeRuns = Measure(3,
                metrics => _planner.PlanAtDuration(ship, "mars", retimeSeconds));
            Run retime = Median(retimeRuns);
            PlanMetrics rm = retime.Metrics;

            WriteRuns("retime", retimeRuns);
            double retimeUninstrumented = TimeUninstrumented(
                () => _planner.PlanAtDuration(ship, "mars", retimeSeconds));
            TestContext.WriteLine(
                $"retime Mars 0.3 g at {retimeDays:F2} d: wall {retime.WallSeconds * 1000.0:F1} ms " +
                $"(instrumentation off: {retimeUninstrumented * 1000.0:F1} ms), " +
                $"allocated {retime.Allocated.Bytes:N0} B " +
                $"({retime.Allocated.Source}), plan " +
                $"{retime.Plan.FlightTimeDays:F5} d, feasible {retime.Plan.IsFeasible}");
            TestContext.WriteLine(
                $"  builds certified {rm.BuildsCertified}, fast {rm.BuildsFastProbe}, " +
                $"probe {rm.BuildsFullFieldProbe}, pin retries {rm.PinRetries}");
            TestContext.WriteLine(
                $"  pin passes {rm.PinPasses} (certified {rm.CertifiedPinPasses})");
            TestContext.WriteLine(
                $"  pin loop {Phase(rm.PinLoopSeconds, retime.WallSeconds)}, " +
                $"brake integration {Phase(rm.PinBrakeSeconds, retime.WallSeconds)}, " +
                $"departure solve {Phase(rm.PinSolveSeconds, retime.WallSeconds)}, " +
                $"settle loop {Phase(rm.SettleLoopSeconds, retime.WallSeconds)}");
            TestContext.WriteLine(
                $"  reported finite-burn arcs {rm.CertifiedBurnArcs} " +
                $"({rm.CertifiedBurnSamples} samples): create " +
                $"{Phase(rm.CertifiedBurnCreateSeconds, retime.WallSeconds)}, " +
                $"measure {Phase(rm.CertifiedBurnMeasureSeconds, retime.WallSeconds)}");
            double retimeRemainder = retime.WallSeconds - rm.PinLoopSeconds
                                     - rm.SettleLoopSeconds
                                     - rm.CertifiedBurnCreateSeconds
                                     - rm.CertifiedBurnMeasureSeconds;
            TestContext.WriteLine(
                $"  untimed remainder {Phase(retimeRemainder, retime.WallSeconds)}");

            Assert.That(retime.Plan.IsFeasible, Is.True,
                retime.Plan.InfeasibleReason);
            Assert.That(retime.Plan.FlightTimeSeconds,
                Is.EqualTo(retimeSeconds).Within(1.0),
                "a fixed-duration build returns the duration it was asked for");
            Assert.That(rm.BuildsCertified, Is.GreaterThanOrEqualTo(1),
                "the reported retime is a certified full-field build");
            Assert.That(rm.PinPasses, Is.GreaterThan(0),
                "the certified build ran its brake/departure fixed point");
            Assert.That(rm.CertifiedBurnArcs, Is.GreaterThanOrEqualTo(2),
                "the plan reports a departure burn and a brake");
            Assert.That(rm.CertifiedBurnSamples, Is.GreaterThan(0));

            // ---- one first plot: PlanFastest, the floor search ----
            _planner.PlanFastest(ship, "mars"); // warm-up
            Run[] plotRuns = Measure(3,
                metrics => _planner.PlanFastest(ship, "mars"));
            Run plot = Median(plotRuns);
            PlanMetrics pm = plot.Metrics;

            WriteRuns("first plot", plotRuns);
            double plotUninstrumented = TimeUninstrumented(
                () => _planner.PlanFastest(ship, "mars"));
            TestContext.WriteLine(
                $"first plot PlanFastest Mars 0.3 g: wall {plot.WallSeconds:F3} s " +
                $"(instrumentation off: {plotUninstrumented:F3} s), " +
                $"allocated {plot.Allocated.Bytes:N0} B " +
                $"({plot.Allocated.Source}), plan " +
                $"{plot.Plan.FlightTimeDays:F5} d, feasible {plot.Plan.IsFeasible}");
            TestContext.WriteLine(
                $"  coast floor {pm.FastestCoastSeconds:F3} s " +
                $"({pm.FastestCoastSeconds / plot.WallSeconds:P1} of wall) at " +
                $"{pm.FastestCoastPlanSeconds / JulianDate.SecondsPerDay:F5} d; " +
                $"burn-only {pm.FastestBurnOnlySeconds:F3} s " +
                $"({pm.FastestBurnOnlySeconds / plot.WallSeconds:P1} of wall) at " +
                $"{pm.FastestBurnOnlyPlanSeconds / JulianDate.SecondsPerDay:F5} d");
            TestContext.WriteLine(
                $"  builds certified {pm.BuildsCertified}, fast {pm.BuildsFastProbe}, " +
                $"probe {pm.BuildsFullFieldProbe}, pin retries {pm.PinRetries}, " +
                $"burn-only builds {pm.BurnOnlyBuilds}");
            TestContext.WriteLine(
                $"  pin passes {pm.PinPasses} (certified {pm.CertifiedPinPasses}, " +
                $"fast {pm.FastPinPasses}, probe {pm.FullFieldProbePinPasses})");
            TestContext.WriteLine(
                $"  pin loop {Phase(pm.PinLoopSeconds, plot.WallSeconds)}, " +
                $"brake integration {Phase(pm.PinBrakeSeconds, plot.WallSeconds)}, " +
                $"departure solve {Phase(pm.PinSolveSeconds, plot.WallSeconds)}, " +
                $"settle loop {Phase(pm.SettleLoopSeconds, plot.WallSeconds)}, " +
                $"burn-only seam {Phase(pm.BurnOnlySeamSeconds, plot.WallSeconds)}");
            TestContext.WriteLine(
                $"  reported finite-burn arcs {pm.CertifiedBurnArcs} " +
                $"({pm.CertifiedBurnSamples} samples): create " +
                $"{Phase(pm.CertifiedBurnCreateSeconds, plot.WallSeconds)}, " +
                $"measure {Phase(pm.CertifiedBurnMeasureSeconds, plot.WallSeconds)}");
            TestContext.WriteLine(
                $"  probe finite-burn arcs {pm.ProbeBurnArcs} " +
                $"({pm.ProbeBurnSamples} samples): create " +
                $"{Phase(pm.ProbeBurnCreateSeconds, plot.WallSeconds)}, " +
                $"measure {Phase(pm.ProbeBurnMeasureSeconds, plot.WallSeconds)}");

            Assert.That(plot.Plan.IsFeasible, Is.True, plot.Plan.InfeasibleReason);
            Assert.That(pm.FastestCalls, Is.EqualTo(1));
            Assert.That(pm.FastestCoastPlanSeconds / JulianDate.SecondsPerDay,
                Is.EqualTo(7.16751).Within(0.05),
                "the first plot's coast floor is the current Mars 0.3 g anchor");
            Assert.That(pm.BurnOnlyBuilds, Is.GreaterThanOrEqualTo(1),
                "the first plot also solves the zero-coast crossing");
            Assert.That(pm.PinPasses, Is.GreaterThan(0),
                "the floor search ran its brake/departure fixed point");
            Assert.That(pm.CertifiedBurnArcs, Is.GreaterThanOrEqualTo(2));
        }
    }
}
