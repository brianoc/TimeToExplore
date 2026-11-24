#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TimeToExplore.EditorTools
{
    /// <summary>Where a benchmark route starts. A station leaves from its own
    /// state; a planet leaves from the parking orbit a conic capture parks in.</summary>
    public enum BenchmarkDepartureKind
    {
        Station,
        ParkingOrbit
    }

    /// <summary>One fixed route the benchmark times. It is a measurement input,
    /// never a planner setting: the level is the player's default unless the
    /// planner refuses the route below it.</summary>
    public sealed class BenchmarkRoute
    {
        public BenchmarkRoute(string name, string displayName, string departureBodyId,
            BenchmarkDepartureKind kind, string targetId, double burnLevelG, string why)
        {
            Name = name;
            DisplayName = displayName;
            DepartureBodyId = departureBodyId;
            Kind = kind;
            TargetId = targetId;
            BurnLevelG = burnLevelG;
            Why = why;
        }

        /// <summary>The stable id the baseline keys on.</summary>
        public string Name { get; }

        public string DisplayName { get; }

        public string DepartureBodyId { get; }

        public BenchmarkDepartureKind Kind { get; }

        public string TargetId { get; }

        public double BurnLevelG { get; }

        /// <summary>Why this level, and where the departure point comes from.</summary>
        public string Why { get; }

        /// <summary>The ship's state at the route's departure point. A station is
        /// its catalogue state; a parking orbit is a circular equatorial orbit at
        /// the radius a conic capture parks in.</summary>
        public ShipState DepartureState(SolarSystemModel model, double julianDate)
        {
            BodyRuntime body = model.Get(DepartureBodyId)
                ?? throw new ArgumentException(
                    $"No body '{DepartureBodyId}' in the catalogue.");
            model.ComputeState(body, julianDate, out Vector3d bodyPosition,
                out Vector3d bodyVelocity);

            if (Kind == BenchmarkDepartureKind.Station)
                return new ShipState(julianDate, bodyPosition, bodyVelocity);

            double radius = ParkingOrbit.ConicRadiusM(body);
            double speed = TwoBody.CircularSpeedMps(body.Gm, radius);

            // Put the ship on the part of the parking orbit whose tangent is
            // prograde relative to the body's own flight. The catalogue's
            // equatorial basis picks an arbitrary phase; a ship about to leave
            // for another planet burns along its motion, and a retrograde phase
            // would test a departure the player does not fly.
            Vector3d planeVelocity = bodyVelocity
                - body.PoleEcliptic * Vector3d.Dot(bodyVelocity, body.PoleEcliptic);
            Vector3d tangent = planeVelocity.SqrMagnitude > 1.0
                ? planeVelocity.Normalized
                : body.EquatorY;
            Vector3d radial = Vector3d.Cross(tangent, body.PoleEcliptic).Normalized;

            return new ShipState(julianDate,
                bodyPosition + radial * radius,
                bodyVelocity + tangent * speed);
        }
    }

    /// <summary>The six routes and their levels. The Earth departures use the
    /// Gateway station, the anchor every existing measurement uses. The reverses
    /// start from the planet's own parking orbit, because that is where a ship
    /// going home actually is.</summary>
    public static class BenchmarkRoutes
    {
        /// <summary>The player's default level, and the level of every route
        /// unless a measured refusal forced a raise.</summary>
        public const double DefaultBurnLevelG = 0.3;

        public const double EpochJulianDate = 2461359.5;

        /// <summary>How far the clock is advanced for the Begin phase. A few
        /// hours is enough to stale the preview without changing the route.</summary>
        public const double BeginAdvanceSeconds = 3.0 * 3600.0;

        public static readonly BenchmarkRoute[] All =
        {
            new BenchmarkRoute("earth-mars", "Earth → Mars", "gateway",
                BenchmarkDepartureKind.Station, "mars", DefaultBurnLevelG,
                "Gateway station, Earth's anchor departure point; 0.3 g is the default level"),
            new BenchmarkRoute("earth-venus", "Earth → Venus", "gateway",
                BenchmarkDepartureKind.Station, "venus", DefaultBurnLevelG,
                "Gateway station; 0.3 g is the default level"),
            new BenchmarkRoute("earth-uranus", "Earth → Uranus", "gateway",
                BenchmarkDepartureKind.Station, "uranus", DefaultBurnLevelG,
                "Gateway station; 0.3 g is the default level"),
            new BenchmarkRoute("mars-earth", "Mars → Earth", "mars",
                BenchmarkDepartureKind.ParkingOrbit, "earth", DefaultBurnLevelG,
                "Mars conic parking orbit; 0.3 g departs a 300 km Mars orbit"),
            new BenchmarkRoute("venus-earth", "Venus → Earth", "venus",
                BenchmarkDepartureKind.ParkingOrbit, "earth", 1.0,
                "Venus conic parking orbit; 0.3 g cannot lift out of Venus's well, so 1 g"),
            new BenchmarkRoute("uranus-earth", "Uranus → Earth", "uranus",
                BenchmarkDepartureKind.ParkingOrbit, "earth", 1.0,
                "Uranus conic parking orbit; 0.3 g cannot lift out of Uranus's well, so 1 g")
        };

        /// <summary>The display name for a route id, falling back to the id so a
        /// baseline recorded by a different build still prints.</summary>
        public static string DisplayNameFor(string route)
        {
            foreach (BenchmarkRoute candidate in All)
            {
                if (string.Equals(candidate.Name, route, StringComparison.Ordinal))
                    return candidate.DisplayName;
            }

            return route;
        }
    }

    /// <summary>
    /// Times the six routes through a plot, both ends of the retime window, and
    /// Begin. It observes the planner and the session; it never changes a
    /// tolerance, a gate, a pass cap, or a returned plan.
    ///
    /// Warm-up rule: one untimed call per case, then three measured runs. The
    /// reported number is the median of the three. The planner, its gravity
    /// field, and the session live across every route, so one-time field setup
    /// is not charged to a measured call.
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
                       "Begin advances the clock three hours, solves the fresh " +
                       "minimum, then retimes to the window maximum when it is " +
                       "above that minimum.",
                routes = new BenchmarkRouteRecord[BenchmarkRoutes.All.Length]
            };

            try
            {
                for (int i = 0; i < BenchmarkRoutes.All.Length; i++)
                {
                    BenchmarkRoute route = BenchmarkRoutes.All[i];
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
            TimeRetime(planner, departure, route.TargetId, midSeconds,
                out TrajectoryPlan midPlan); // warm-up
            var midRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
                midRuns[i] = TimeRetime(planner, departure, route.TargetId,
                    midSeconds, out midPlan);

            TimeRetime(planner, departure, route.TargetId, maximum,
                out TrajectoryPlan maxPlan); // warm-up
            var maxRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
                maxRuns[i] = TimeRetime(planner, departure, route.TargetId,
                    maximum, out maxPlan);

            // ---- Begin: PlanFastest at a moved date, then the chosen retime ----
            double beginDate = epoch + BenchmarkRoutes.BeginAdvanceSeconds
                / JulianDate.SecondsPerDay;
            ShipState moved = departure.WithJulianDate(beginDate);
            TimeBeginMinimum(planner, moved, route.TargetId,
                out TrajectoryPlan beginPlan); // warm-up
            var beginRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
            {
                beginRuns[i] = TimeBeginMinimum(planner, moved, route.TargetId,
                    out beginPlan);
            }

            bool beginsWithRetime = maximum > beginPlan.FlightTimeSeconds;
            var beginRetimeRuns = new double[MeasuredRuns];
            var beginTotalRuns = new double[MeasuredRuns];
            if (beginsWithRetime)
            {
                TimeRetime(planner, moved, route.TargetId, maximum,
                    out TrajectoryPlan beginRetimed); // warm-up
                for (int i = 0; i < MeasuredRuns; i++)
                {
                    beginRetimeRuns[i] = TimeRetime(planner, moved, route.TargetId,
                        maximum, out beginRetimed);
                    beginTotalRuns[i] = beginRuns[i] + beginRetimeRuns[i];
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
                       ? "begin retimes to the window maximum"
                       : "begin stays at the fresh minimum");

            Debug.Log($"{route.Name} on {machine}: " +
                      $"plot {Median(plotRuns):F0} ms, " +
                      $"retime mid {Median(midRuns):F0} ms, " +
                      $"retime max {Median(maxRuns):F0} ms, " +
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

        private static double TimePlot(TripPlanner planner, FlightSession session,
            ShipState departure, string target, out TrajectoryPlan plan,
            out double maximumSeconds)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanFastest(departure, target);
            maximumSeconds = session.MaximumPreviewSeconds(target,
                plan.FlightTimeSeconds, planner, departure);
            return Milliseconds(started);
        }

        private static double TimeRetime(TripPlanner planner, ShipState departure,
            string target, double seconds, out TrajectoryPlan plan)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanAtDuration(departure, target, seconds);
            return Milliseconds(started);
        }

        private static double TimeBeginMinimum(TripPlanner planner, ShipState departure,
            string target, out TrajectoryPlan plan)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanFastest(departure, target);
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
