using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Runs the route benchmark inside a built player when the command line
    /// carries <c>-tteBench</c>. It covers the plot, both retimes, and Begin;
    /// the Editor benchmark also times the drag because it needs the session's
    /// worker. It writes JSON to <c>-tteBenchOut &lt;path&gt;</c> and quits.
    ///
    /// This exists to answer one question: does a CoreCLR player build reach the
    /// plain .NET Release numbers, or does it keep the Editor's JIT profile?
    /// </summary>
    public static class PlayerRouteBenchmark
    {
        private const int MeasuredRuns = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!HasFlag(args, "-tteBench")) return;

            int exitCode = 1;
            try
            {
                Run(ArgumentValue(args, "-tteBenchOut"));
                exitCode = 0;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                Application.Quit(exitCode);
            }
        }

        private static void Run(string outputPath)
        {
            TextAsset catalogue = Resources.Load<TextAsset>("solar_system");
            if (catalogue == null)
                throw new InvalidOperationException("solar_system.json is not in Resources.");

            var model = new SolarSystemModel();
            model.Load(catalogue.text);
            var gravity = new GravityField(model);
            var planner = new TripPlanner(model, gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            var session = new FlightSession(model, gravity, planner);
            var routes = new List<RouteResult>();

            try
            {
                foreach (BenchmarkRoute route in BenchmarkRoutes.All)
                {
                    planner.BurnLevelG = route.BurnLevelG;
                    RouteResult result = MeasureRoute(model, planner, session, route);
                    routes.Add(result);
                    Debug.Log($"[player-bench] {route.Name}: plot " +
                              $"{result.plotMs:F0} ms, retime mid " +
                              $"{result.retimeMidMs:F0} ms, retime max " +
                              $"{result.retimeMaxMs:F0} ms, begin " +
                              $"{result.beginMs:F0} ms");
                }
            }
            finally
            {
                session.Dispose();
            }

            var debug = typeof(TripPlanner).Assembly
                .GetCustomAttribute<DebuggableAttribute>();
            string json = Serialize(routes, debug);
            string path = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(Application.persistentDataPath, "player-bench.json")
                : Path.GetFullPath(outputPath);
            File.WriteAllText(path, json);
            Debug.Log("[player-bench] wrote " + path);
            Debug.Log(json);
        }

        private static RouteResult MeasureRoute(SolarSystemModel model,
            TripPlanner planner, FlightSession session, BenchmarkRoute route)
        {
            double epoch = BenchmarkRoutes.EpochJulianDate;
            ShipState departure = route.DepartureState(model, epoch);

            TimePlot(planner, session, departure, route.TargetId,
                out TrajectoryPlan plotPlan, out double maximum); // warm-up
            var plotRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
                plotRuns[i] = TimePlot(planner, session, departure, route.TargetId,
                    out plotPlan, out _);

            var result = new RouteResult
            {
                route = route.Name,
                displayName = route.DisplayName,
                burnLevelG = route.BurnLevelG,
                plotMs = Median(plotRuns)
            };
            if (!plotPlan.IsFeasible)
            {
                result.note = "plot refused: " + plotPlan.InfeasibleReason;
                return result;
            }

            double floorSeconds = plotPlan.FlightTimeSeconds;
            double midSeconds = floorSeconds + 0.5 * (maximum - floorSeconds);
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

            double beginDate = epoch + BenchmarkRoutes.BeginAdvanceSeconds
                / JulianDate.SecondsPerDay;
            session.PlaceAt(route.DepartureBodyId, epoch);
            session.BurnLevelG = route.BurnLevelG;
            session.ArrivalStyle = CaptureStyle.Conic;
            PreparePreview(session, route.TargetId, epoch, double.NaN);
            TimeSessionBegin(session, route.TargetId, beginDate, out _); // warm-up
            var beginRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
            {
                PreparePreview(session, route.TargetId, epoch, double.NaN);
                beginRuns[i] = TimeSessionBegin(session, route.TargetId, beginDate,
                    out _);
            }

            result.floorDays = plotPlan.FlightTimeDays;
            result.windowMaxDays = maximum / JulianDate.SecondsPerDay;
            result.retimeMidMs = Median(midRuns);
            result.retimeMaxMs = Median(maxRuns);
            result.beginMs = Median(beginRuns);
            return result;
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
            string target, double seconds, out TrajectoryPlan plan, PinSeed seed)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanAtDuration(departure, target, seconds, seed.Nearby(seconds));
            return Milliseconds(started);
        }

        private static void PreparePreview(FlightSession session, string target,
            double epoch, double seconds)
        {
            session.Plot(target, epoch);
            if (double.IsFinite(seconds)) session.SetPreviewDuration(seconds);
        }

        private static double TimeSessionBegin(FlightSession session, string target,
            double julianDate, out TrajectoryPlan plan)
        {
            long started = Stopwatch.GetTimestamp();
            plan = session.Begin(target, julianDate);
            return Milliseconds(started);
        }

        private static double Milliseconds(long started) =>
            (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        private static double Median(double[] runs)
        {
            var sorted = (double[])runs.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static string Serialize(List<RouteResult> routes,
            DebuggableAttribute debug)
        {
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"runtime\": \"").Append(Environment.Version).Append("\",\n");
            json.Append("  \"framework\": \"")
                .Append(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription)
                .Append("\",\n");
            json.Append("  \"optimizerDisabled\": ")
                .Append(debug != null && debug.IsJITOptimizerDisabled ? "true" : "false")
                .Append(",\n");
            json.Append("  \"isDebugBuild\": ").Append(UnityEngine.Debug.isDebugBuild ? "true" : "false")
                .Append(",\n");
            json.Append("  \"routes\": [\n");
            for (int r = 0; r < routes.Count; r++)
            {
                RouteResult route = routes[r];
                json.Append("    {\"route\": \"").Append(route.route)
                    .Append("\", \"displayName\": \"").Append(route.displayName)
                    .Append("\", \"burnLevelG\": ")
                    .Append(route.burnLevelG.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"floorDays\": ")
                    .Append(route.floorDays.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"windowMaxDays\": ")
                    .Append(route.windowMaxDays.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"phases\": [");
                AppendPhase(json, "plot", route.plotMs, true);
                if (route.retimeMidMs > 0.0)
                {
                    AppendPhase(json, "retime-mid", route.retimeMidMs, false);
                    AppendPhase(json, "retime-max", route.retimeMaxMs, false);
                    AppendPhase(json, "begin-min", route.beginMs, false);
                }
                json.Append("]}");
                if (r + 1 < routes.Count) json.Append(',');
                json.Append('\n');
            }
            json.Append("  ]\n}\n");
            return json.ToString();
        }

        private static void AppendPhase(StringBuilder json, string name, double ms,
            bool first)
        {
            if (!first) json.Append(", ");
            json.Append("{\"phase\": \"").Append(name).Append("\", \"milliseconds\": ")
                .Append(ms.ToString("F3", CultureInfo.InvariantCulture)).Append('}');
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
                if (string.Equals(arg, flag, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string ArgumentValue(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];
            return null;
        }

        private sealed class RouteResult
        {
            public string route;
            public string displayName;
            public double burnLevelG;
            public double floorDays;
            public double windowMaxDays;
            public double plotMs;
            public double retimeMidMs;
            public double retimeMaxMs;
            public double beginMs;
            public string note;
        }
    }
}
