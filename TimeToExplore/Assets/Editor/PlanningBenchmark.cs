#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// Times the six routes through a plot, both ends of the retime window, a
    /// drag of eight consecutive slider steps, and Begin. It observes the
    /// planner and the session; it never changes a tolerance, a gate, a pass
    /// cap, or a returned plan.
    ///
    /// Warm-up rule: one untimed call per case, then three measured runs. The
    /// reported number is the median of the three. The drag is one sequence:
    /// each step's answer depends on what the prefetch filled before it, so a
    /// repeat would measure a different cache. It reports the median and the
    /// worst of its eight steps instead. The planner, its gravity field, and the
    /// session live across every route, so one-time field setup is not charged
    /// to a measured call.
    ///
    /// A plot is <see cref="TripPlanner.PlanFastest"/> followed by
    /// <see cref="FlightSession.MaximumPreviewSeconds"/>, the pair
    /// <see cref="FlightSession"/>'s window build runs. The benchmark calls the
    /// pair directly, so it measures a cold window every time rather than the
    /// session's cache: the player's four-second wait is the first plot.
    /// </summary>
    public static class PlanningBenchmark
    {
        private const int MeasuredRuns = 3;

        /// <summary>How many consecutive slider steps the drag phase requests
        /// from the window minimum. Eight is a short drag: enough to pass the
        /// two neighbours a plot prefetched and to see the worker fall behind or
        /// stay ahead of the handle.</summary>
        private const int DragSteps = 8;

        /// <summary>When a plot is refused, one fixed-duration build at this many
        /// days tells the next session whether the trip exists at all or the
        /// geometry refuses it. It is a diagnosis, never a timed phase.</summary>
        private const int DiagnosticDays = 90;

        [MenuItem("Tools/Time to Explore/Run Route Benchmark")]
        public static void RunFromMenu() => Execute(false);

        /// <summary>The batch-mode entry point: <c>-executeMethod
        /// TimeToExplore.EditorTools.PlanningBenchmark.Run</c>.</summary>
        public static void Run() => Execute(true);

        private static void Execute(bool exitBatch)
        {
            try
            {
                RunCore();
                Debug.Log("Planning benchmark complete.");
                if (exitBatch) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Console.Error.WriteLine("Planning benchmark failed: " + error);
                if (exitBatch) EditorApplication.Exit(1);
            }
        }

        private static void RunCore()
        {
            string dataJson = File.ReadAllText(Path.Combine(
                Application.dataPath, "Resources", "solar_system.json"));
            var model = new SolarSystemModel();
            model.Load(dataJson);

            var gravity = new GravityField(model);
            var planner = new TripPlanner(model, gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            if (Environment.GetEnvironmentVariable("TTE_BENCH_TRACE") == "1")
                planner.Trace = message => Debug.Log("[bench] " + message);
            var session = new FlightSession(model, gravity, planner);

            var run = new BenchmarkBaseline
            {
                machine = Environment.MachineName,
                unityVersion = Application.unityVersion,
                cpu = DescribeProcessor(),
                operatingSystem = SystemInfo.operatingSystem,
                recordedAt = DateTime.UtcNow.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                note = $"JD {BenchmarkRoutes.EpochJulianDate:F1}, conic capture. " +
                       "Plot is PlanFastest plus the cold window build. Retimes " +
                       "are PlanAtDuration at the window middle and maximum. " +
                       "Drag requests eight consecutive slider steps from the " +
                       "window minimum on the session's own worker, prefetch on, " +
                       "and reports each request's time to publish. " +
                       "Begin plots at the epoch, advances the clock three " +
                       "hours, then calls the session's Begin: inside the " +
                       "preview's freshness window it flies the drawn plan, " +
                       "outside it replans; the '@max' phase retimes the " +
                       "preview to the window maximum first.",
                routes = new BenchmarkRouteRecord[0]
            };

            var selected = new List<BenchmarkRoute>();
            string onlyRoute = Environment.GetEnvironmentVariable("TTE_BENCH_ROUTE");
            foreach (BenchmarkRoute candidate in BenchmarkRoutes.All)
            {
                if (string.IsNullOrEmpty(onlyRoute) || candidate.Name == onlyRoute)
                    selected.Add(candidate);
            }

            if (selected.Count == 0)
                throw new ArgumentException($"No benchmark route named '{onlyRoute}'.");

            run.routes = new BenchmarkRouteRecord[selected.Count];

            var debug = typeof(TripPlanner).Assembly
                .GetCustomAttribute<DebuggableAttribute>();
            Debug.Log($"[bench] runtime {Environment.Version}, " +
                      $"{RuntimeInformation.FrameworkDescription}, " +
                      $"optimizerDisabled={debug?.IsJITOptimizerDisabled}, " +
                      $"routes={selected.Count}");

            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    BenchmarkRoute route = selected[i];
                    planner.BurnLevelG = route.BurnLevelG;
                    run.routes[i] = MeasureRoute(model, planner, session, route,
                        run.machine);
                }
            }
            finally
            {
                session.Dispose();
            }

            string baselineJson = ReadOptional(Environment.GetEnvironmentVariable(
                "TTE_BENCH_BASELINE"));
            BenchmarkBaseline baseline = null;
            if (baselineJson != null
                && !BenchmarkReport.TryParse(baselineJson, out baseline,
                    out string parseError))
            {
                Debug.LogWarning("Ignoring the baseline: " + parseError);
                baseline = null;
            }

            bool comparable = BenchmarkReport.IsComparable(baseline, run.machine,
                run.unityVersion);
            bool color = Environment.GetEnvironmentVariable("TTE_BENCH_COLOR") == "1";
            string table = BenchmarkReport.FormatTable(run, baseline, comparable, color);

            string tablePath = Environment.GetEnvironmentVariable("TTE_BENCH_TABLE");
            WriteOptional(tablePath, table);
            WriteOptional(Environment.GetEnvironmentVariable("TTE_BENCH_RESULTS"),
                BenchmarkReport.Serialize(run));

            if (Environment.GetEnvironmentVariable("TTE_BENCH_RECORD") == "1")
            {
                string baselinePath = Environment.GetEnvironmentVariable(
                    "TTE_BENCH_BASELINE");
                if (string.IsNullOrEmpty(baselinePath))
                    throw new InvalidOperationException(
                        "TTE_BENCH_BASELINE is required to record.");
                if (baseline != null && !comparable)
                {
                    Debug.LogWarning($"Recording a baseline from {run.machine} over " +
                                     $"one recorded on {baseline.machine}; the deltas " +
                                     "above compared different machines.");
                }

                WriteOptional(baselinePath, BenchmarkReport.Serialize(run));
            }

            Console.WriteLine(table);
            Debug.Log(table);
        }

        private static BenchmarkRouteRecord MeasureRoute(SolarSystemModel model,
            TripPlanner planner, FlightSession session, BenchmarkRoute route,
            string machine)
        {
            double epoch = BenchmarkRoutes.EpochJulianDate;
            ShipState departure = route.DepartureState(model, epoch);

            // ---- plot: PlanFastest plus the cold window build ----
            TimePlot(planner, session, departure, route.TargetId,
                out TrajectoryPlan floor, out double maximum); // warm-up
            var plotRuns = new double[MeasuredRuns];
            TrajectoryPlan plotPlan = floor;
            for (int i = 0; i < MeasuredRuns; i++)
            {
                plotRuns[i] = TimePlot(planner, session, departure, route.TargetId,
                    out plotPlan, out _);
            }

            var phases = new List<BenchmarkPhaseRecord>
            {
                Phase(BenchmarkPhases.Plot, plotRuns)
            };
            string note;

            if (!plotPlan.IsFeasible)
            {
                // A refused plot has no window, so a retime and Begin would ask
                // for a duration the search never found. The refusal is recorded
                // with its reason rather than turned into numbers.
                string refusal = Refusal(plotPlan);
                note = route.Why + "; plot refused (" + refusal +
                       "); retime and begin are not measured";
                double diagnosticSeconds = DiagnosticDays
                                           * JulianDate.SecondsPerDay;
                TrajectoryPlan diagnostic = planner.PlanAtDuration(departure,
                    route.TargetId, diagnosticSeconds);
                note += diagnostic.IsFeasible
                    ? "; a fixed " + DiagnosticDays + " d build is feasible at " +
                      diagnostic.FlightTimeDays.ToString("F5",
                          CultureInfo.InvariantCulture) + " d"
                    : "; a fixed " + DiagnosticDays + " d build is refused too (" +
                      Refusal(diagnostic) + ")";

                Debug.Log($"{route.Name} on {machine}: plot refused in " +
                          $"{Median(plotRuns):F0} ms ({refusal}); retime and " +
                          "begin not measured.");

                return new BenchmarkRouteRecord
                {
                    route = route.Name,
                    displayName = route.DisplayName,
                    burnLevelG = route.BurnLevelG,
                    floorDays = 0.0,
                    windowMaxDays = 0.0,
                    note = note,
                    phases = phases.ToArray()
                };
            }

            double floorSeconds = plotPlan.FlightTimeSeconds;
            double midSeconds = floorSeconds + 0.5 * (maximum - floorSeconds);

            // ---- retime at the window middle and at its maximum ----
            // Each retime starts from the pin the nearest solved trip settled
            // on — the window plan for the middle, the middle for the maximum —
            // exactly as the session's cache hands one over. The cold build it
            // replaces is the same call with a default seed.
            PinSeed floorSeed = plotPlan.PinSeed;
            TimeRetime(planner, departure, route.TargetId, midSeconds,
                out TrajectoryPlan midPlan, floorSeed); // warm-up
            var midRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
                midRuns[i] = TimeRetime(planner, departure, route.TargetId,
                    midSeconds, out midPlan, floorSeed);

            PinSeed midSeed = midPlan.PinSeed;
            TimeRetime(planner, departure, route.TargetId, maximum,
                out TrajectoryPlan maxPlan, midSeed); // warm-up
            var maxRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
                maxRuns[i] = TimeRetime(planner, departure, route.TargetId,
                    maximum, out maxPlan, midSeed);

            // ---- drag: eight consecutive steps on the real worker ----
            // Prefetch is on. The first two steps are the neighbours the plot
            // prefetched; each request then queues three steps of its own in the
            // direction of travel. The phase's latency is request to publish, so
            // a cache hit reads as the pump's own cost and a build reads as the
            // build.
            DragResult drag = MeasureDrag(session, route, epoch);
            double dragMedian = drag != null ? Median(drag.Milliseconds) : 0.0;
            double dragWorst = drag != null ? Worst(drag.Milliseconds) : 0.0;
            if (drag != null)
            {
                phases.Add(new BenchmarkPhaseRecord
                {
                    phase = BenchmarkPhases.DragMedian,
                    milliseconds = dragMedian
                });
                phases.Add(new BenchmarkPhaseRecord
                {
                    phase = BenchmarkPhases.DragWorst,
                    milliseconds = dragWorst
                });
            }

            // ---- Begin: the session's own path on a plotted preview ----
            // The preview is plotted at the epoch (untimed setup, already
            // measured by the plot phase) and Begin is asked three hours later.
            // What is timed is FlightSession.Begin itself: inside the preview's
            // freshness window it flies the drawn plan, outside it replans. The
            // "@max" phase retimes the preview to the window's right end before
            // beginning, which is the player's longer trip.
            double beginDate = epoch + BenchmarkRoutes.BeginAdvanceSeconds
                / JulianDate.SecondsPerDay;
            session.PlaceAt(route.DepartureBodyId, epoch);
            session.BurnLevelG = route.BurnLevelG;
            session.ArrivalStyle = CaptureStyle.Conic;
            PreparePreview(session, route.TargetId, epoch, double.NaN);
            TimeSessionBegin(session, route.TargetId, beginDate,
                out TrajectoryPlan beginPlan); // warm-up
            var beginRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
            {
                PreparePreview(session, route.TargetId, epoch, double.NaN);
                beginRuns[i] = TimeSessionBegin(session, route.TargetId, beginDate,
                    out beginPlan);
            }

            bool beginsWithRetime = maximum > beginPlan.FlightTimeSeconds;
            var beginRetimeRuns = new double[MeasuredRuns];
            var beginTotalRuns = new double[MeasuredRuns];
            if (beginsWithRetime)
            {
                PreparePreview(session, route.TargetId, epoch, maximum); // warm-up
                TimeSessionBegin(session, route.TargetId, beginDate, out _);
                for (int i = 0; i < MeasuredRuns; i++)
                {
                    PreparePreview(session, route.TargetId, epoch, maximum);
                    beginTotalRuns[i] = TimeSessionBegin(session, route.TargetId,
                        beginDate, out _);
                    beginRetimeRuns[i] = Math.Max(0.0,
                        beginTotalRuns[i] - beginRuns[i]);
                }
            }
            else
            {
                Array.Copy(beginRuns, beginTotalRuns, MeasuredRuns);
            }

            phases.Add(Phase(BenchmarkPhases.RetimeMid, midRuns));
            phases.Add(Phase(BenchmarkPhases.RetimeMax, maxRuns));
            phases.Add(Phase(BenchmarkPhases.BeginMinimum, beginRuns));
            phases.Add(Phase(BenchmarkPhases.BeginRetime, beginRetimeRuns));
            phases.Add(Phase(BenchmarkPhases.BeginTotal, beginTotalRuns));

            note = route.Why + "; " + State(plotPlan, "plot") + ", "
                   + State(midPlan, "retime mid") + ", "
                   + State(maxPlan, "retime max") + ", "
                   + State(beginPlan, "begin") + "; "
                   + (beginsWithRetime
                       ? "begin flies the drawn minimum; its @max phase retimes " +
                         "the preview to the window maximum first"
                       : "begin stays at the drawn minimum");
            if (drag != null)
            {
                note += "; drag of " + DragSteps + " steps: " + dragMedian.ToString("F0",
                            CultureInfo.InvariantCulture) + " ms median, " +
                        dragWorst.ToString("F0", CultureInfo.InvariantCulture) +
                        " ms worst, " + drag.Builds + " built, " + drag.CacheHits +
                        " from cache, " + drag.Prefetched + " prefetched";
            }

            Debug.Log($"{route.Name} on {machine}: " +
                      $"plot {Median(plotRuns):F0} ms, " +
                      $"retime mid {Median(midRuns):F0} ms, " +
                      $"retime max {Median(maxRuns):F0} ms, " +
                      $"drag {dragMedian:F0}/{dragWorst:F0} ms " +
                      $"({drag?.Builds ?? 0} built, {drag?.CacheHits ?? 0} cached), " +
                      $"steps {Describe(drag?.Milliseconds)}, " +
                      $"begin {Median(beginRuns):F0} ms, " +
                      $"begin total {Median(beginTotalRuns):F0} ms.");

            return new BenchmarkRouteRecord
            {
                route = route.Name,
                displayName = route.DisplayName,
                burnLevelG = route.BurnLevelG,
                floorDays = floorSeconds / JulianDate.SecondsPerDay,
                windowMaxDays = maximum / JulianDate.SecondsPerDay,
                note = note,
                phases = phases.ToArray()
            };
        }

        private static string State(TrajectoryPlan plan, string label)
        {
            if (plan == null) return label + " did not build";
            if (!plan.IsFeasible)
            {
                return label + " infeasible (" + Refusal(plan) + ")";
            }

            return label + " " + plan.FlightTimeDays.ToString("F5",
                CultureInfo.InvariantCulture) + " d";
        }

        private static string Refusal(TrajectoryPlan plan)
        {
            if (plan == null) return "no plan";
            return string.IsNullOrEmpty(plan.InfeasibleReason)
                ? "infeasible"
                : plan.InfeasibleReason;
        }

        private static BenchmarkPhaseRecord Phase(string name, double[] runs) =>
            new BenchmarkPhaseRecord { phase = name, milliseconds = Median(runs) };

        private static double Median(double[] runs)
        {
            var sorted = (double[])runs.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static double Worst(double[] runs)
        {
            double worst = double.NegativeInfinity;
            foreach (double run in runs) worst = Math.Max(worst, run);
            return double.IsNaN(worst) ? double.NaN : worst;
        }

        /// <summary>
        /// The drag phase: plot on the real session, wait for the prefetch the
        /// plot queued, then request eight consecutive steps from the window
        /// minimum, each after the previous publish. Prefetch is on, so the
        /// first steps may be answered from the cache and every request queues
        /// three steps of its own ahead. Returns null when the session refuses.
        /// </summary>
        private static DragResult MeasureDrag(FlightSession session, BenchmarkRoute route,
            double epoch)
        {
            session.PrefetchEnabled =
                Environment.GetEnvironmentVariable("TTE_BENCH_NO_PREFETCH") != "1";
            session.PlaceAt(route.DepartureBodyId, epoch);
            session.BurnLevelG = route.BurnLevelG;
            session.ArrivalStyle = CaptureStyle.Conic;
            session.DiscardPreview();

            if (!session.PlotAsync(route.TargetId, epoch)
                || !WaitForPreview(session, out TrajectoryPlan plotted)
                || !plotted.IsFeasible)
            {
                session.DiscardPreview();
                return null;
            }

            // The plot's own ±2 neighbours: a drag that starts as soon as the
            // route appears must not race them or time them.
            WaitForPrefetchIdle(session);

            double minimum = session.PreviewMinimumSeconds;
            double maximum = session.PreviewMaximumSeconds;
            double step = (maximum - minimum) / (FlightSession.SliderStepCount - 1);
            if (!(step > 0.0) || !double.IsFinite(step))
            {
                session.DiscardPreview();
                return null;
            }

            int builds = session.RetimeBuilds;
            int cacheHits = session.RetimeCacheHits;
            int prefetched = session.PrefetchBuilds;

            var latencies = new double[DragSteps];
            for (int i = 1; i <= DragSteps; i++)
            {
                double seconds = minimum + step * i;
                long started = Stopwatch.GetTimestamp();
                if (!session.RequestPreviewDuration(seconds) || !WaitForRetime(session))
                {
                    session.DiscardPreview();
                    return null;
                }

                latencies[i - 1] = Milliseconds(started);
            }

            var result = new DragResult
            {
                Milliseconds = latencies,
                Builds = session.RetimeBuilds - builds,
                CacheHits = session.RetimeCacheHits - cacheHits,
                Prefetched = session.PrefetchBuilds - prefetched
            };

            // Leave no background work running while the next route's phases are
            // timed: prefetch shares the one worker.
            session.DiscardPreview();
            WaitForPrefetchIdle(session);
            return result;
        }

        private static bool WaitForPreview(FlightSession session,
            out TrajectoryPlan plan)
        {
            plan = null;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 120.0)
            {
                if (session.TryApplyCompletedPreview(out plan)) return true;
                Thread.Sleep(1);
            }

            return false;
        }

        private static bool WaitForRetime(FlightSession session)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 60.0)
            {
                if (session.TryApplyCompletedRetime(out _)) return true;
                Thread.Sleep(1);
            }

            return false;
        }

        private static void WaitForPrefetchIdle(FlightSession session)
        {
            var watch = Stopwatch.StartNew();
            while (session.IsPrefetchPending && watch.Elapsed.TotalSeconds < 60.0)
                Thread.Sleep(1);
        }

        private static double TimePlot(TripPlanner planner, FlightSession session,
            ShipState departure, string target, out TrajectoryPlan plan,
            out double maximumSeconds)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanFastest(departure, target, out TrajectoryPlan coastFloor);
            double floor = coastFloor != null && coastFloor.IsFeasible
                           && coastFloor.FlightTimeSeconds > 0.0
                ? coastFloor.FlightTimeSeconds
                : plan.FlightTimeSeconds;
            maximumSeconds = session.MaximumPreviewSeconds(target, floor,
                planner, departure);
            return Milliseconds(started);
        }

        private static double TimeRetime(TripPlanner planner, ShipState departure,
            string target, double seconds, out TrajectoryPlan plan,
            PinSeed seed = default)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanAtDuration(departure, target, seconds,
                seed.Nearby(seconds));
            return Milliseconds(started);
        }

        /// <summary>Plot the route on the session so Begin has a preview, and
        /// retime it when a finite duration is given. Untimed setup: the plot
        /// phase already measured the plot, and the session's window cache makes
        /// a repeat plot of the same route free.</summary>
        private static void PreparePreview(FlightSession session, string target,
            double epoch, double seconds)
        {
            session.Plot(target, epoch);
            if (double.IsFinite(seconds)) session.SetPreviewDuration(seconds);
        }

        /// <summary>Time <see cref="FlightSession.Begin"/> itself: the player's
        /// press, including the preview-reuse decision and any replan it
        /// forces.</summary>
        private static double TimeSessionBegin(FlightSession session, string target,
            double julianDate, out TrajectoryPlan plan)
        {
            long started = Stopwatch.GetTimestamp();
            plan = session.Begin(target, julianDate);
            return Milliseconds(started);
        }

        private static double Milliseconds(long started) =>
            (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        private static string DescribeProcessor()
        {
            string processor = SystemInfo.processorType;
            if (string.IsNullOrWhiteSpace(processor)) processor = "unknown CPU";
            return processor + ", " + SystemInfo.processorCount + " cores, " +
                   SystemInfo.systemMemorySize + " MB";
        }

        /// <summary>One drag's request-to-publish latencies and what the worker
        /// had to do for them.</summary>
        private sealed class DragResult
        {
            public double[] Milliseconds;
            public int Builds;
            public int CacheHits;
            public int Prefetched;
        }

        private static string Describe(double[] values)
        {
            if (values == null || values.Length == 0) return "none";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) text.Append(' ');
                text.Append(values[i].ToString("F0", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        private static string ReadOptional(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return File.ReadAllText(path);
        }

        private static void WriteOptional(string path, string text)
        {
            if (string.IsNullOrEmpty(path)) return;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, text);
        }
    }
}
#endif
