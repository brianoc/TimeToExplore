#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TimeToExplore.EditorTools
{
    /// <summary>The phase names the route benchmark records. They are stable
    /// strings because they key the committed baseline file.</summary>
    public static class BenchmarkPhases
    {
        /// <summary>A first plot: <c>PlanFastest</c> plus the window build.</summary>
        public const string Plot = "plot";

        /// <summary>One retime at the middle of the trip-time window.</summary>
        public const string RetimeMid = "retime-mid";

        /// <summary>One retime at the window maximum, the slider's right end.</summary>
        public const string RetimeMax = "retime-max";

        /// <summary>A drag's request-to-publish latency, the median of eight
        /// consecutive steps from the window minimum, prefetch on.</summary>
        public const string DragMedian = "drag-median";

        /// <summary>The same drag's slowest request-to-publish step.</summary>
        public const string DragWorst = "drag-worst";

        /// <summary>Begin's fresh minimum: <c>PlanFastest</c> at the moved date.</summary>
        public const string BeginMinimum = "begin-min";

        /// <summary>Begin's retime to the chosen duration, when it is above the
        /// fresh minimum.</summary>
        public const string BeginRetime = "begin-retime";

        /// <summary>The two Begin calls together, which is what the player waits
        /// for when they press Go.</summary>
        public const string BeginTotal = "begin-total";

        /// <summary>The eight phases in table order.</summary>
        public static readonly string[] All =
        {
            Plot, RetimeMid, RetimeMax, DragMedian, DragWorst,
            BeginMinimum, BeginRetime, BeginTotal
        };

        /// <summary>The column headings, in the same order as <see cref="All"/>.</summary>
        public static readonly string[] Headings =
        {
            "plot", "retime mid", "retime max", "drag med", "drag worst",
            "begin", "begin @max", "begin total"
        };
    }

    [Serializable]
    public sealed class BenchmarkPhaseRecord
    {
        public string phase;
        public double milliseconds;
    }

    [Serializable]
    public sealed class BenchmarkRouteRecord
    {
        public string route;
        public string displayName;
        public double burnLevelG;
        public double floorDays;
        public double windowMaxDays;
        public string note;
        public BenchmarkPhaseRecord[] phases;
    }

    /// <summary>One run's numbers, and the committed baseline's. The same shape
    /// serves both so a run can become the baseline without a conversion.</summary>
    [Serializable]
    public sealed class BenchmarkBaseline
    {
        public int schema = BenchmarkReport.Schema;
        public string machine;
        public string unityVersion;
        public string cpu;
        public string operatingSystem;
        public string recordedAt;
        public string note;
        public BenchmarkRouteRecord[] routes;
    }

    /// <summary>The baseline file's format, the delta arithmetic, and the
    /// plain-text report. No timing is decided here; the numbers arrive from
    /// <see cref="PlanningBenchmark"/>.</summary>
    public static class BenchmarkReport
    {
        public const int Schema = 1;

        /// <summary>A change at or above this is shouted about. Below it the
        /// report stays quiet: editor timings move by several percent on their
        /// own, so a smaller gate would cry wolf.</summary>
        public const double RegressionThresholdPercent = 25.0;

        private const string Reset = "\u001b[0m";
        private const string Red = "\u001b[31m";
        private const string Green = "\u001b[32m";

        public static string Serialize(BenchmarkBaseline baseline) =>
            JsonUtility.ToJson(baseline, true);

        /// <summary>Parse a baseline, refusing anything that is not one. A
        /// malformed file must fail loudly: a benchmark that quietly compares
        /// against nothing is worse than no benchmark.</summary>
        public static bool TryParse(string json, out BenchmarkBaseline baseline,
            out string error)
        {
            baseline = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the baseline file is empty";
                return false;
            }

            try
            {
                baseline = JsonUtility.FromJson<BenchmarkBaseline>(json);
            }
            catch (Exception exception)
            {
                error = "the baseline is not valid JSON: " + exception.Message;
                return false;
            }

            if (baseline == null)
            {
                error = "the baseline is not a benchmark baseline";
                return false;
            }

            if (baseline.schema != Schema)
            {
                error = $"the baseline schema is {baseline.schema}, expected {Schema}";
                baseline = null;
                return false;
            }

            if (string.IsNullOrEmpty(baseline.machine)
                || string.IsNullOrEmpty(baseline.unityVersion)
                || baseline.routes == null || baseline.routes.Length == 0)
            {
                error = "the baseline has no machine, Unity version, or routes";
                baseline = null;
                return false;
            }

            return true;
        }

        /// <summary>Percentage change from the baseline to the run. A phase that
        /// did not run in either is zero; a phase that did not run in the
        /// baseline has no percentage.</summary>
        public static double PercentChange(double current, double baseline)
        {
            if (!double.IsFinite(current) || !double.IsFinite(baseline))
                return double.NaN;
            if (baseline == 0.0) return current == 0.0 ? 0.0 : double.NaN;
            return (current - baseline) / baseline * 100.0;
        }

        public static bool IsRegression(double percent) =>
            double.IsFinite(percent) && percent >= RegressionThresholdPercent;

        /// <summary>True only when the baseline was recorded on this machine and
        /// Unity version. A delta across machines measures the machines, not the
        /// code.</summary>
        public static bool IsComparable(BenchmarkBaseline baseline, string machine,
            string unityVersion) =>
            baseline != null
            && string.Equals(baseline.machine, machine, StringComparison.Ordinal)
            && string.Equals(baseline.unityVersion, unityVersion,
                StringComparison.Ordinal);

        /// <summary>The recorded median for a route and phase, or NaN.</summary>
        public static double LookupMilliseconds(BenchmarkBaseline baseline,
            string route, string phase)
        {
            if (baseline?.routes == null) return double.NaN;
            foreach (BenchmarkRouteRecord record in baseline.routes)
            {
                if (!string.Equals(record.route, route, StringComparison.Ordinal)
                    || record.phases == null) continue;
                foreach (BenchmarkPhaseRecord entry in record.phases)
                {
                    if (string.Equals(entry.phase, phase, StringComparison.Ordinal))
                        return entry.milliseconds;
                }
            }

            return double.NaN;
        }

        public static string FormatDelta(double percent)
        {
            if (double.IsNaN(percent)) return "n/a";
            return percent.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>The whole plain-text report: a header, a column per phase in
        /// milliseconds with its percentage delta, the route context, and any
        /// regression at or above the threshold.</summary>
        public static string FormatTable(BenchmarkBaseline run, BenchmarkBaseline baseline,
            bool comparable, bool color)
        {
            var text = new StringBuilder();
            text.AppendLine("Planning benchmark — route planning cost");
            if (!string.IsNullOrEmpty(run.note)) text.AppendLine(run.note);
            text.AppendLine($"Machine: {run.machine} ({run.cpu})   " +
                            $"Unity {run.unityVersion}   {run.operatingSystem}");
            text.AppendLine($"Run date: {run.recordedAt}   " +
                            $"warm-up: one untimed call per case, then median of three");
            if (baseline == null)
            {
                text.AppendLine("Baseline: none committed yet — deltas unavailable. " +
                                "Run tools/bench.sh --record to write one.");
            }
            else
            {
                text.AppendLine($"Baseline: {baseline.recordedAt} on {baseline.machine}, " +
                                $"Unity {baseline.unityVersion}");
                if (!comparable)
                {
                    text.AppendLine("WARNING: the baseline was recorded on another " +
                                    "machine or Unity version. The deltas compare the " +
                                    "machines, not the code, and are not meaningful.");
                }
            }

            text.AppendLine();
            text.Append("route".PadRight(20));
            text.Append("level".PadRight(8));
            foreach (string heading in BenchmarkPhases.Headings)
                text.Append(heading.PadLeft(18));
            text.Append("total".PadLeft(9));
            text.AppendLine();

            foreach (BenchmarkRouteRecord route in run.routes)
            {
                text.Append((route.displayName ?? route.route).PadRight(20));
                text.Append((route.burnLevelG.ToString("0.0",
                    CultureInfo.InvariantCulture) + " g").PadRight(8));
                double runTotal = 0.0, baselineTotal = 0.0;
                bool compared = false;
                foreach (string phase in BenchmarkPhases.All)
                {
                    double milliseconds = Lookup(route, phase);
                    double previous = LookupMilliseconds(baseline, route.route, phase);
                    double percent = PercentChange(milliseconds, previous);
                    text.Append(Cell(milliseconds, percent, color).PadLeft(18));
                    if (double.IsFinite(milliseconds) && double.IsFinite(previous))
                    {
                        runTotal += milliseconds;
                        baselineTotal += previous;
                        compared = true;
                    }
                }

                text.Append(DeltaCell(compared
                    ? PercentChange(runTotal, baselineTotal)
                    : double.NaN, color).PadLeft(9));
                text.AppendLine();
            }

            var regressions = new List<string>();
            foreach (BenchmarkRouteRecord route in run.routes)
            {
                foreach (string phase in BenchmarkPhases.All)
                {
                    double percent = PercentChange(Lookup(route, phase),
                        LookupMilliseconds(baseline, route.route, phase));
                    if (IsRegression(percent))
                    {
                        regressions.Add($"{route.displayName ?? route.route} / " +
                                        $"{phase}: {FormatDelta(percent)}");
                    }
                }
            }

            text.AppendLine();
            if (regressions.Count == 0)
            {
                text.AppendLine($"No phase regressed by {RegressionThresholdPercent:F0}% " +
                                "or more.");
            }
            else
            {
                text.AppendLine($"!! {regressions.Count} regression(s) at or above " +
                                $"{RegressionThresholdPercent:F0}%:");
                foreach (string line in regressions) text.AppendLine("   " + line);
            }

            text.AppendLine();
            text.AppendLine("Context (a phase's delta is only meaningful for the same route):");
            text.Append("route".PadRight(20));
            text.Append("min d".PadLeft(12));
            text.Append("window d".PadLeft(12));
            text.AppendLine("  note");
            foreach (BenchmarkRouteRecord route in run.routes)
            {
                text.Append((route.displayName ?? route.route).PadRight(20));
                text.Append(Minutes(route.floorDays).PadLeft(12));
                text.Append(Minutes(route.windowMaxDays).PadLeft(12));
                text.AppendLine("  " + route.note);
            }

            return text.ToString();
        }

        private static double Lookup(BenchmarkRouteRecord route, string phase)
        {
            if (route.phases == null) return double.NaN;
            foreach (BenchmarkPhaseRecord entry in route.phases)
            {
                if (string.Equals(entry.phase, phase, StringComparison.Ordinal))
                    return entry.milliseconds;
            }

            return double.NaN;
        }

        private static string Cell(double milliseconds, double percent, bool color)
        {
            if (double.IsNaN(milliseconds)) return "—".PadLeft(18);
            string value = milliseconds.ToString("N0", CultureInfo.InvariantCulture)
                .PadLeft(7) + " ms";
            string delta = FormatDelta(percent).PadLeft(7);
            return Colorise(value + " " + delta, percent, color);
        }

        private static string DeltaCell(double percent, bool color) =>
            Colorise(FormatDelta(percent), percent, color);

        private static string Colorise(string text, double percent, bool color)
        {
            if (!color || double.IsNaN(percent)) return text;
            if (percent > 0.0) return Red + text + Reset;
            if (percent < 0.0) return Green + text + Reset;
            return text;
        }

        private static string Minutes(double days) =>
            days > 0.0
                ? days.ToString("F5", CultureInfo.InvariantCulture)
                : "—";
    }
}
#endif
