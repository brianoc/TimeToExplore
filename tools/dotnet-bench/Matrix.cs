using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.DotnetBench
{
    /// <summary>
    /// The plot-time matrix: every major planet to every other major planet,
    /// measured on the plain .NET host. Plot means the same pair the session's
    /// first preview runs — <see cref="TripPlanner.PlanFastest"/> plus the cold
    /// window build. A route the default level refuses is retried at 1 g, which
    /// is the level the existing benchmark already uses where 0.3 g cannot lift
    /// out of the well.
    /// </summary>
    internal static class MatrixBench
    {
        private const double EpochJulianDate = 2461359.5;
        private const double DefaultBurnLevelG = 0.3;
        private const double RetryBurnLevelG = 1.0;

        /// <summary>The eight planets, with the body a departure actually
        /// starts from. Earth leaves from the Gateway station, the anchor the
        /// existing benchmark uses; the others leave from their parking
        /// orbit.</summary>
        private static readonly (string Id, string Name, string DepartureId)[] Planets =
        {
            ("mercury", "Mercury", "mercury"),
            ("venus", "Venus", "venus"),
            ("earth", "Earth", "gateway"),
            ("mars", "Mars", "mars"),
            ("jupiter", "Jupiter", "jupiter"),
            ("saturn", "Saturn", "saturn"),
            ("uranus", "Uranus", "uranus"),
            ("neptune", "Neptune", "neptune")
        };

        internal static int Run(string dataPath, string outputPath, string markdownPath,
            int runs, string fromFilter, string toFilter)
        {
            if (runs < 1) throw new ArgumentException("--runs must be at least 1.");

            var model = new SolarSystemModel();
            model.Load(File.ReadAllText(dataPath));

            var gravity = new GravityField(model);
            var planner = new TripPlanner(model, gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            var metrics = new PlanMetrics();
            planner.Metrics = metrics;
            var session = new FlightSession(model, gravity, planner);

            var run = new MatrixRun
            {
                runtime = ".NET " + Environment.Version + " (CoreCLR)",
                machine = Environment.MachineName,
                cpu = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                      + ", " + Environment.ProcessorCount + " cores",
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                recordedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz",
                    CultureInfo.InvariantCulture),
                configuration = AppContext.BaseDirectory.Contains("/Debug/",
                    StringComparison.Ordinal) ? "Debug" : "Release",
                epochJulianDate = EpochJulianDate,
                runs = runs,
                levelPolicy = "0.3 g (the player's default); a refusal is retried at 1 g",
                cells = new List<MatrixCell>()
            };

            try
            {
                foreach ((string fromId, string fromName, string departureId) in Planets)
                {
                    if (!string.IsNullOrEmpty(fromFilter) && fromId != fromFilter) continue;
                    foreach ((string toId, string toName, _) in Planets)
                    {
                        if (fromId == toId) continue;
                        if (!string.IsNullOrEmpty(toFilter) && toId != toFilter) continue;
                        MatrixCell cell = Measure(model, planner, session, metrics,
                            fromId, fromName, departureId, toId, toName, runs);
                        run.cells.Add(cell);
                        Console.Error.WriteLine(FormatProgress(cell));
                    }
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
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, json);
            Console.Error.WriteLine("Wrote " + outputPath);

            string markdown = ToMarkdown(run);
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath) ?? ".");
            File.WriteAllText(markdownPath, markdown);
            Console.Error.WriteLine("Wrote " + markdownPath);
            Console.WriteLine(markdown);

            return 0;
        }

        /// <summary>One fixed-duration coast build, for checking whether a
        /// refused route is refused by the search's duration seed or by the
        /// physics. Prints the verdict rather than timing it.</summary>
        internal static int Probe(string dataPath, string fromId, string toId,
            double days, double levelG)
        {
            if (!(days > 0.0)) throw new ArgumentException("--probe-days must be positive.");

            var model = new SolarSystemModel();
            model.Load(File.ReadAllText(dataPath));
            var planner = new TripPlanner(model, new GravityField(model))
            {
                ArrivalStyle = CaptureStyle.Conic,
                BurnLevelG = levelG
            };
            ShipState departure = DepartureState(model, DepartureIdFor(fromId),
                EpochJulianDate);

            double seconds = days * JulianDate.SecondsPerDay;
            Console.WriteLine($"{fromId} → {toId}, {days:F2} d at {levelG:0.###} g");
            long started = Stopwatch.GetTimestamp();
            TrajectoryPlan coast = planner.PlanCoastAtDuration(departure, toId, seconds);
            double coastMs =
                (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Print("coast", coast, coastMs);

            started = Stopwatch.GetTimestamp();
            TrajectoryPlan full = planner.PlanAtDuration(departure, toId, seconds);
            double fullMs =
                (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Print("full", full, fullMs);

            return coast.IsFeasible || full.IsFeasible ? 0 : 1;
        }

        private static void Print(string label, TrajectoryPlan plan, double milliseconds)
        {
            Console.WriteLine($"  {label,-5} feasible {plan.IsFeasible,-5} "
                + $"flight {plan.FlightTimeDays:F5} d, "
                + $"delta-v {plan.DeltaVMps:F1} m/s, {milliseconds:F0} ms");
            if (!plan.IsFeasible) Console.WriteLine("        reason: " + plan.InfeasibleReason);
        }

        private static string DepartureIdFor(string bodyId) =>
            string.Equals(bodyId, "earth", StringComparison.Ordinal) ? "gateway" : bodyId;

        private static MatrixCell Measure(SolarSystemModel model, TripPlanner planner,
            FlightSession session, PlanMetrics metrics, string fromId, string fromName,
            string departureId, string toId, string toName, int runs)
        {
            ShipState departure = DepartureState(model, departureId, EpochJulianDate);

            var cell = new MatrixCell
            {
                from = fromId,
                fromName = fromName,
                to = toId,
                toName = toName,
                levelG = DefaultBurnLevelG
            };

            Measurement attempt = Plot(planner, session, metrics, departure, toId,
                DefaultBurnLevelG, runs);
            cell.feasible = attempt.Feasible;
            cell.plotMs = attempt.Milliseconds;
            cell.floorDays = Finite(attempt.FloorDays);
            cell.windowDays = Finite(attempt.WindowDays);
            cell.reason = attempt.Reason;
            cell.phases = attempt.Phases;
            cell.buildsCertified = attempt.buildsCertified;
            cell.buildsFastProbe = attempt.buildsFastProbe;
            cell.buildsFullFieldProbe = attempt.buildsFullFieldProbe;
            cell.pinPasses = attempt.pinPasses;
            cell.certifiedPinPasses = attempt.certifiedPinPasses;
            cell.fastPinPasses = attempt.fastPinPasses;
            cell.certifiedBurnArcs = attempt.certifiedBurnArcs;
            cell.certifiedBurnSamples = attempt.certifiedBurnSamples;
            cell.probeBurnArcs = attempt.probeBurnArcs;
            cell.probeBurnSamples = attempt.probeBurnSamples;

            if (!attempt.Feasible)
            {
                Measurement retry = Plot(planner, session, metrics, departure, toId,
                    RetryBurnLevelG, runs);
                cell.retryLevelG = RetryBurnLevelG;
                cell.retryFeasible = retry.Feasible;
                cell.retryPlotMs = retry.Milliseconds;
                cell.retryFloorDays = Finite(retry.FloorDays);
                cell.retryWindowDays = Finite(retry.WindowDays);
                cell.retryReason = retry.Reason;
                cell.retryPhases = retry.Phases;
                cell.retryBuildsCertified = retry.buildsCertified;
                cell.retryBuildsFastProbe = retry.buildsFastProbe;
                cell.retryBuildsFullFieldProbe = retry.buildsFullFieldProbe;
                cell.retryPinPasses = retry.pinPasses;
                cell.retryCertifiedPinPasses = retry.certifiedPinPasses;
                cell.retryFastPinPasses = retry.fastPinPasses;
                cell.retryCertifiedBurnArcs = retry.certifiedBurnArcs;
                cell.retryCertifiedBurnSamples = retry.certifiedBurnSamples;
            }

            return cell;
        }

        private static Measurement Plot(TripPlanner planner, FlightSession session,
            PlanMetrics metrics, ShipState departure, string target, double levelG,
            int runs)
        {
            planner.BurnLevelG = levelG;

            // Warm-up: one untimed call, then the measured median.
            PlotOnce(planner, session, metrics, departure, target, out _, out _);
            var times = new double[runs];
            TrajectoryPlan plan = null;
            double maximum = double.NaN;
            for (int i = 0; i < runs; i++)
            {
                metrics.Reset();
                times[i] = PlotOnce(planner, session, metrics, departure, target,
                    out plan, out maximum);
            }

            var measurement = new Measurement
            {
                Milliseconds = Median(times),
                Feasible = plan != null && plan.IsFeasible,
                FloorDays = plan != null && plan.IsFeasible && plan.FlightTimeSeconds > 0.0
                    ? plan.FlightTimeDays
                    : double.NaN,
                WindowDays = maximum / JulianDate.SecondsPerDay,
                Reason = plan == null ? "no plan"
                    : plan.IsFeasible
                        ? null
                        : string.IsNullOrEmpty(plan.InfeasibleReason)
                            ? "infeasible"
                            : plan.InfeasibleReason,
                Phases = new List<MatrixPhase>()
            };

            AddPhase(measurement.Phases, "pin-loop", metrics.PinLoopSeconds * 1000.0);
            AddPhase(measurement.Phases, "pin-brake", metrics.PinBrakeSeconds * 1000.0);
            AddPhase(measurement.Phases, "pin-solve", metrics.PinSolveSeconds * 1000.0);
            AddPhase(measurement.Phases, "settle-loop", metrics.SettleLoopSeconds * 1000.0);
            AddPhase(measurement.Phases, "burn-only-seam", metrics.BurnOnlySeamSeconds * 1000.0);
            AddPhase(measurement.Phases, "certified-burn-create",
                metrics.CertifiedBurnCreateSeconds * 1000.0);
            AddPhase(measurement.Phases, "certified-burn-measure",
                metrics.CertifiedBurnMeasureSeconds * 1000.0);
            AddPhase(measurement.Phases, "probe-burn-create",
                metrics.ProbeBurnCreateSeconds * 1000.0);
            AddPhase(measurement.Phases, "probe-burn-measure",
                metrics.ProbeBurnMeasureSeconds * 1000.0);
            AddPhase(measurement.Phases, "coast-floor",
                metrics.FastestCoastSeconds * 1000.0);
            AddPhase(measurement.Phases, "burn-only-search",
                metrics.FastestBurnOnlySeconds * 1000.0);

            measurement.buildsCertified = metrics.BuildsCertified;
            measurement.buildsFastProbe = metrics.BuildsFastProbe;
            measurement.buildsFullFieldProbe = metrics.BuildsFullFieldProbe;
            measurement.pinPasses = metrics.PinPasses;
            measurement.certifiedPinPasses = metrics.CertifiedPinPasses;
            measurement.fastPinPasses = metrics.FastPinPasses;
            measurement.certifiedBurnArcs = metrics.CertifiedBurnArcs;
            measurement.certifiedBurnSamples = metrics.CertifiedBurnSamples;
            measurement.probeBurnArcs = metrics.ProbeBurnArcs;
            measurement.probeBurnSamples = metrics.ProbeBurnSamples;
            return measurement;
        }

        private static void AddPhase(List<MatrixPhase> phases, string name,
            double milliseconds)
        {
            if (milliseconds <= 0.0) return;
            phases.Add(new MatrixPhase { phase = name, milliseconds = milliseconds });
        }

        private static double PlotOnce(TripPlanner planner, FlightSession session,
            PlanMetrics metrics, ShipState departure, string target,
            out TrajectoryPlan plan, out double maximumSeconds)
        {
            long started = Stopwatch.GetTimestamp();
            plan = planner.PlanFastest(departure, target, out TrajectoryPlan coastFloor);
            double floor = coastFloor != null && coastFloor.IsFeasible
                           && coastFloor.FlightTimeSeconds > 0.0
                ? coastFloor.FlightTimeSeconds
                : plan.FlightTimeSeconds;
            maximumSeconds = session.MaximumPreviewSeconds(target, floor, planner,
                departure);
            return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>The same departure the Editor benchmark uses: the Gateway
        /// station for Earth, the conic parking orbit for every other planet,
        /// with the orbit's phase prograde relative to the planet's own flight.
        /// </summary>
        private static ShipState DepartureState(SolarSystemModel model, string bodyId,
            double julianDate)
        {
            BodyRuntime body = model.Get(bodyId)
                ?? throw new ArgumentException($"No body '{bodyId}'.");
            model.ComputeState(body, julianDate, out Vector3d bodyPosition,
                out Vector3d bodyVelocity);

            if (body.Data.IsStation)
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

        private static string FormatProgress(MatrixCell cell)
        {
            double level = cell.feasible ? cell.levelG : (cell.retryLevelG ?? 0.0);
            bool feasible = cell.feasible || cell.retryFeasible == true;
            double ms = cell.feasible ? cell.plotMs : (cell.retryPlotMs ?? 0.0);
            string verdict = feasible
                ? $"{ms:F0} ms at {level:0.#} g"
                : "refused at 1 g (" + cell.retryReason + ")";
            return $"{cell.fromName} → {cell.toName}: {verdict}";
        }

        private static string ToMarkdown(MatrixRun run)
        {
            var text = new StringBuilder();
            text.AppendLine("| From \\ To | " + string.Join(" | ",
                Array.ConvertAll(Planets, p => p.Name)) + " |");
            text.AppendLine("|---|" + string.Concat(Repeat("---:|", Planets.Length)));
            foreach ((string fromId, string fromName, _) in Planets)
            {
                var row = new StringBuilder();
                row.Append("| **").Append(fromName).Append("** |");
                foreach ((string toId, _, _) in Planets)
                {
                    if (fromId == toId)
                    {
                        row.Append(" — |");
                        continue;
                    }

                    MatrixCell cell = run.cells.Find(c => c.from == fromId && c.to == toId);
                    row.Append(' ').Append(cell == null ? "—" : CellText(cell)).Append(" |");
                }

                text.AppendLine(row.ToString());
            }

            text.AppendLine();
            text.AppendLine("Recorded " + run.recordedAt + " on " + run.machine + " ("
                + run.cpu + "), " + run.runtime + " " + run.configuration
                + ", JD " + run.epochJulianDate.ToString("F1", CultureInfo.InvariantCulture)
                + ", median of " + run.runs + ". " + run.levelPolicy
                + " A * marks a 1 g plot after the 0.3 g attempt refused.");
            text.AppendLine();

            var refusals = new List<MatrixCell>();
            foreach (MatrixCell cell in run.cells)
            {
                if (!cell.feasible && cell.retryFeasible != true) refusals.Add(cell);
            }

            if (refusals.Count > 0)
            {
                text.AppendLine("Refused at both levels:");
                foreach (MatrixCell cell in refusals)
                {
                    text.AppendLine("* " + cell.fromName + " → " + cell.toName + ": "
                        + cell.retryReason + ".");
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        private static string CellText(MatrixCell cell)
        {
            if (cell.feasible)
                return cell.plotMs.ToString("F0", CultureInfo.InvariantCulture) + " ms";
            if (cell.retryFeasible == true)
                return (cell.retryPlotMs ?? 0.0).ToString("F0", CultureInfo.InvariantCulture)
                       + " ms*";
            return "refused";
        }

        private static IEnumerable<string> Repeat(string value, int count)
        {
            for (int i = 0; i < count; i++) yield return value;
        }

        private static double? Finite(double value) =>
            double.IsFinite(value) ? value : (double?)null;

        private static double Median(double[] runs)
        {
            var sorted = (double[])runs.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private sealed class Measurement
        {
            internal double Milliseconds;
            internal bool Feasible;
            internal double FloorDays;
            internal double WindowDays;
            internal string Reason;
            internal List<MatrixPhase> Phases;

            internal int buildsCertified;
            internal int buildsFastProbe;
            internal int buildsFullFieldProbe;
            internal int pinPasses;
            internal int certifiedPinPasses;
            internal int fastPinPasses;
            internal int certifiedBurnArcs;
            internal int certifiedBurnSamples;
            internal int probeBurnArcs;
            internal int probeBurnSamples;
        }
    }

    internal sealed class MatrixRun
    {
        public string runtime { get; set; }
        public string machine { get; set; }
        public string cpu { get; set; }
        public string operatingSystem { get; set; }
        public string recordedAt { get; set; }
        public string configuration { get; set; }
        public double epochJulianDate { get; set; }
        public int runs { get; set; }
        public string levelPolicy { get; set; }
        public List<MatrixCell> cells { get; set; }
    }

    internal sealed class MatrixCell
    {
        public string from { get; set; }
        public string fromName { get; set; }
        public string to { get; set; }
        public string toName { get; set; }
        public double levelG { get; set; }
        public bool feasible { get; set; }
        public double plotMs { get; set; }
        public double? floorDays { get; set; }
        public double? windowDays { get; set; }
        public string reason { get; set; }
        public List<MatrixPhase> phases { get; set; }
        public double? retryLevelG { get; set; }
        public bool? retryFeasible { get; set; }
        public double? retryPlotMs { get; set; }
        public double? retryFloorDays { get; set; }
        public double? retryWindowDays { get; set; }
        public string retryReason { get; set; }
        public List<MatrixPhase> retryPhases { get; set; }
        public int buildsCertified { get; set; }
        public int buildsFastProbe { get; set; }
        public int buildsFullFieldProbe { get; set; }
        public int pinPasses { get; set; }
        public int certifiedPinPasses { get; set; }
        public int fastPinPasses { get; set; }
        public int certifiedBurnArcs { get; set; }
        public int certifiedBurnSamples { get; set; }
        public int probeBurnArcs { get; set; }
        public int probeBurnSamples { get; set; }
        public int? retryBuildsCertified { get; set; }
        public int? retryBuildsFastProbe { get; set; }
        public int? retryBuildsFullFieldProbe { get; set; }
        public int? retryPinPasses { get; set; }
        public int? retryCertifiedPinPasses { get; set; }
        public int? retryFastPinPasses { get; set; }
        public int? retryCertifiedBurnArcs { get; set; }
        public int? retryCertifiedBurnSamples { get; set; }
    }

    internal sealed class MatrixPhase
    {
        public string phase { get; set; }
        public double milliseconds { get; set; }
    }
}
