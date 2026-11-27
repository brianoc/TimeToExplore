using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.DotnetBench
{
    /// <summary>One fixed route. This mirrors the Editor's
    /// <c>BenchmarkRoutes.All</c>; <c>tools/test_dotnet_bench.py</c> fails the
    /// tooling suite if the two lists drift apart.</summary>
    internal sealed class Route
    {
        internal Route(string name, string displayName, string departureBodyId,
            bool station, string targetId, double burnLevelG, string why)
        {
            Name = name;
            DisplayName = displayName;
            DepartureBodyId = departureBodyId;
            Station = station;
            TargetId = targetId;
            BurnLevelG = burnLevelG;
            Why = why;
        }

        internal string Name { get; }
        internal string DisplayName { get; }
        internal string DepartureBodyId { get; }
        internal bool Station { get; }
        internal string TargetId { get; }
        internal double BurnLevelG { get; }
        internal string Why { get; }

        internal ShipState DepartureState(SolarSystemModel model, double julianDate)
        {
            BodyRuntime body = model.Get(DepartureBodyId)
                ?? throw new ArgumentException($"No body '{DepartureBodyId}'.");

            model.ComputeState(body, julianDate, out Vector3d bodyPosition,
                out Vector3d bodyVelocity);

            if (Station)
                return new ShipState(julianDate, bodyPosition, bodyVelocity);

            double radius = ParkingOrbit.ConicRadiusM(body);
            double speed = TwoBody.CircularSpeedMps(body.Gm, radius);

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

    internal static class Program
    {
        private const double DefaultBurnLevelG = 0.3;
        private const double EpochJulianDate = 2461359.5;
        private const double BeginAdvanceSeconds = 3.0 * 3600.0;
        private const int MeasuredRuns = 3;
        private const int DragSteps = 8;

        private static readonly Route[] Routes =
        {
            new Route("earth-mars", "Earth → Mars", "gateway", true, "mars",
                DefaultBurnLevelG, "Gateway station"),
            new Route("earth-venus", "Earth → Venus", "gateway", true, "venus",
                DefaultBurnLevelG, "Gateway station"),
            new Route("earth-uranus", "Earth → Uranus", "gateway", true, "uranus",
                DefaultBurnLevelG, "Gateway station"),
            new Route("mars-earth", "Mars → Earth", "mars", false, "earth",
                DefaultBurnLevelG, "Mars conic parking orbit"),
            new Route("venus-earth", "Venus → Earth", "venus", false, "earth",
                1.0, "Venus conic parking orbit"),
            new Route("uranus-earth", "Uranus → Earth", "uranus", false, "earth",
                1.0, "Uranus conic parking orbit")
        };

        private static int Main(string[] args)
        {
            string root = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(root) &&
                   !File.Exists(Path.Combine(root, "tools", "bench.sh")))
            {
                root = Path.GetDirectoryName(root);
            }

            if (string.IsNullOrEmpty(root))
                root = Directory.GetCurrentDirectory();

            string dataPath = Path.GetFullPath(Path.Combine(root,
                "TimeToExplore", "Assets", "Resources", "solar_system.json"));
            string outputPath = null;
            bool micro = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data" && i + 1 < args.Length)
                    dataPath = Path.GetFullPath(args[++i]);
                else if (args[i] == "--out" && i + 1 < args.Length)
                    outputPath = Path.GetFullPath(args[++i]);
                else if (args[i] == "--micro")
                    micro = true;
            }

            if (micro)
            {
                Trig(1_000_000);
                VectorLoop(1_000_000);
                double ms = 0.0;
                ms += Measure("trig", Trig);
                ms += Measure("vector", VectorLoop);
                Console.Error.WriteLine($"micro total {ms:F1} ms");
                return 0;
            }

            var model = new SolarSystemModel();
            model.Load(File.ReadAllText(dataPath));

            var gravity = new GravityField(model);
            var planner = new TripPlanner(model, gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            var session = new FlightSession(model, gravity, planner);

            var run = new RuntimeRun
            {
                runtime = ".NET " + Environment.Version + " (CoreCLR)",
                machine = Environment.MachineName,
                cpu = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                      + ", " + Environment.ProcessorCount + " cores",
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                recordedAt = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                configuration = AppContext.BaseDirectory.Contains("/Debug/", StringComparison.Ordinal)
                    ? "Debug" : "Release",
                debuggable = DescribeDebuggable(),
                tieredCompilation = Switch("System.Runtime.TieredCompilation"),
                tieredPgo = Switch("System.Runtime.TieredPGO"),
                serverGc = System.Runtime.GCSettings.IsServerGC,
                routes = new List<RouteRun>()
            };

            try
            {
                foreach (Route route in Routes)
                {
                    planner.BurnLevelG = route.BurnLevelG;
                    RouteRun measured = MeasureRoute(model, planner, session, route);
                    run.routes.Add(measured);
                    var summary = new List<string>();
                    foreach (Phase phase in measured.phases)
                        summary.Add($"{phase.phase} {phase.milliseconds:F0} ms");
                    Console.Error.WriteLine($"{route.Name}: " + string.Join(", ", summary));
                }
            }
            finally
            {
                session.Dispose();
            }

            string json = JsonSerializer.Serialize(run, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            });
            if (outputPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
                File.WriteAllText(outputPath, json);
                Console.Error.WriteLine("Wrote " + outputPath);
            }
            else
            {
                Console.WriteLine(json);
            }

            return 0;
        }

        private static RouteRun MeasureRoute(SolarSystemModel model,
            TripPlanner planner, FlightSession session, Route route)
        {
            double epoch = EpochJulianDate;
            ShipState departure = route.DepartureState(model, epoch);

            TimePlot(planner, session, departure, route.TargetId, out TrajectoryPlan floor,
                out double maximum); // warm-up
            var plotRuns = new double[MeasuredRuns];
            TrajectoryPlan plotPlan = floor;
            for (int i = 0; i < MeasuredRuns; i++)
                plotRuns[i] = TimePlot(planner, session, departure, route.TargetId,
                    out plotPlan, out _);

            var result = new RouteRun
            {
                route = route.Name,
                displayName = route.DisplayName,
                burnLevelG = route.BurnLevelG,
                phases = new List<Phase>()
            };
            result.phases.Add(new Phase { phase = "plot", milliseconds = Median(plotRuns) });

            if (!plotPlan.IsFeasible)
            {
                result.note = route.Why + "; plot refused ("
                    + (string.IsNullOrEmpty(plotPlan.InfeasibleReason)
                        ? "infeasible"
                        : plotPlan.InfeasibleReason) + ")";
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
                maxRuns[i] = TimeRetime(planner, departure, route.TargetId, maximum,
                    out maxPlan, midSeed);

            DragResult drag = MeasureDrag(session, route, epoch);

            double beginDate = epoch + BeginAdvanceSeconds / JulianDate.SecondsPerDay;
            session.PlaceAt(route.DepartureBodyId, epoch);
            session.BurnLevelG = route.BurnLevelG;
            session.ArrivalStyle = CaptureStyle.Conic;
            PreparePreview(session, route.TargetId, epoch, double.NaN);
            TimeSessionBegin(session, route.TargetId, beginDate, out TrajectoryPlan beginPlan);
            var beginRuns = new double[MeasuredRuns];
            for (int i = 0; i < MeasuredRuns; i++)
            {
                PreparePreview(session, route.TargetId, epoch, double.NaN);
                beginRuns[i] = TimeSessionBegin(session, route.TargetId, beginDate,
                    out beginPlan);
            }

            result.floorDays = plotPlan.FlightTimeDays;
            result.retimeMidDays = midPlan.FlightTimeDays;
            result.retimeMaxDays = maxPlan.FlightTimeDays;
            result.phases.Add(new Phase { phase = "retime-mid", milliseconds = Median(midRuns) });
            result.phases.Add(new Phase { phase = "retime-max", milliseconds = Median(maxRuns) });
            result.phases.Add(new Phase { phase = "begin-min", milliseconds = Median(beginRuns) });
            if (drag != null)
            {
                result.phases.Add(new Phase
                {
                    phase = "drag-median",
                    milliseconds = Median(drag.Milliseconds)
                });
                result.phases.Add(new Phase
                {
                    phase = "drag-worst",
                    milliseconds = Worst(drag.Milliseconds)
                });
            }

            result.beginDays = beginPlan?.FlightTimeDays ?? double.NaN;
            return result;
        }

        private static DragResult MeasureDrag(FlightSession session, Route route,
            double epoch)
        {
            session.PrefetchEnabled = true;
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

            WaitForPrefetchIdle(session);

            double minimum = session.PreviewMinimumSeconds;
            double maximum = session.PreviewMaximumSeconds;
            double step = (maximum - minimum) / (FlightSession.SliderStepCount - 1);
            if (!(step > 0.0) || !double.IsFinite(step))
            {
                session.DiscardPreview();
                return null;
            }

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

            var result = new DragResult { Milliseconds = latencies };
            session.DiscardPreview();
            WaitForPrefetchIdle(session);
            return result;
        }

        private static bool WaitForPreview(FlightSession session, out TrajectoryPlan plan)
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
            plan = planner.PlanFastest(departure, target);
            maximumSeconds = session.MaximumPreviewSeconds(target, plan.FlightTimeSeconds,
                planner, departure);
            return Milliseconds(started);
        }

        private static double TimeRetime(TripPlanner planner, ShipState departure,
            string target, double seconds, out TrajectoryPlan plan,
            PinSeed seed = default)
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

        private static double Worst(double[] runs)
        {
            double worst = double.NegativeInfinity;
            foreach (double run in runs) worst = Math.Max(worst, run);
            return worst;
        }

        /// <summary>The JIT-optimizer flag the build wrote into the assembly.
        /// A Debug build disables it; the Editor's assemblies do the same.</summary>
        private static string DescribeDebuggable()
        {
            var attribute = typeof(Program).Assembly
                .GetCustomAttribute<DebuggableAttribute>();
            if (attribute == null) return "none";
            return "tracking=" + attribute.IsJITTrackingEnabled
                + ", optimizerDisabled=" + attribute.IsJITOptimizerDisabled;
        }

        private static string Switch(string name) =>
            AppContext.TryGetSwitch(name, out bool value) ? value.ToString() : "(unset)";

        private static double Measure(string name, Func<int, double> body)
        {
            var watch = Stopwatch.StartNew();
            double result = body(10_000_000);
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            Console.Error.WriteLine($"[micro] {name}: {ms:F1} ms, " +
                                    $"{ms * 1e6 / 10_000_000:F2} ns/op");
            return result;
        }

        internal static double Trig(int iterations)
        {
            double x = 0.1, y = 0.2, z = 0.3, acc = 0.0;
            for (int i = 0; i < iterations; i++)
            {
                double r = Math.Sqrt(x * x + y * y + z * z);
                double s = Math.Sin(x) + Math.Cos(y) + Math.Atan2(z, x);
                acc += r + s;
                x += 1e-7;
                y += 2e-7;
                z += 3e-7;
                if (x > 1.0)
                {
                    x -= 1.0;
                    y -= 1.0;
                    z -= 1.0;
                }
            }

            return acc;
        }

        internal static double VectorLoop(int iterations)
        {
            var v = new Vector3d(1.0, 2.0, 3.0);
            var w = new Vector3d(0.1, 0.2, 0.3);
            Vector3d acc = Vector3d.Zero;
            for (int i = 0; i < iterations; i++)
            {
                v = (v + w).Normalized;
                acc += Vector3d.Cross(v, w) + v * 0.5;
            }

            return acc.SqrMagnitude;
        }

        private sealed class DragResult
        {
            internal double[] Milliseconds;
        }
    }

    internal sealed class RuntimeRun
    {
        public string runtime { get; set; }
        public string machine { get; set; }
        public string cpu { get; set; }
        public string operatingSystem { get; set; }
        public string recordedAt { get; set; }
        public string configuration { get; set; }
        public string debuggable { get; set; }
        public string tieredCompilation { get; set; }
        public string tieredPgo { get; set; }
        public bool serverGc { get; set; }
        public List<RouteRun> routes { get; set; }
    }

    internal sealed class RouteRun
    {
        public string route { get; set; }
        public string displayName { get; set; }
        public double burnLevelG { get; set; }
        public double floorDays { get; set; }
        public double retimeMidDays { get; set; }
        public double retimeMaxDays { get; set; }
        public double beginDays { get; set; }
        public List<Phase> phases { get; set; }
        public string note { get; set; }
    }

    internal sealed class Phase
    {
        public string phase { get; set; }
        public double milliseconds { get; set; }
    }
}
