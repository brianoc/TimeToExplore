using System;
using System.Collections.Generic;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>
    /// Builds the flight panel: the plan's phase table and the live execution
    /// rows. Pure data and formatting, like <see cref="BodyDetails"/>, so the
    /// content is verified without opening a scene.
    /// </summary>
    public static class FlightReport
    {
        /// <summary>
        /// Rows for the flight panel. <paramref name="warpLabel"/> is the HUD's
        /// own clock label, passed in so this class does not need the clock.
        /// <paramref name="currentJulianDate"/> is used only to age a route
        /// preview; NaN (the default) reports a fresh preview.
        /// </summary>
        public static List<DetailRow> Build(FlightSession session, SolarSystemModel model,
            string warpLabel, double currentJulianDate = double.NaN)
        {
            var rows = new List<DetailRow>();
            if (session == null) return rows;

            TrajectoryPlan plan = session.Plan ?? session.Preview;
            if (plan == null) return rows;

            BodyRuntime target = model?.Get(plan.TargetId);

            rows.Add(DetailRow.Heading("PLAN"));
            rows.Add(DetailRow.Of("Target", target != null ? target.Name : plan.TargetId));

            foreach (PlanArc arc in plan.Arcs)
            {
                rows.Add(DetailRow.Of(PhaseLabel(arc.Phase),
                    $"{JulianDate.FormatShort(arc.StartJulianDate)}  ·  " +
                    SolarSystemHud.FormatDuration(arc.DurationSeconds)));
            }

            rows.Add(DetailRow.Of("Arrival", JulianDate.FormatShort(plan.ArrivalJulianDate)));
            rows.Add(DetailRow.Of("Flight time", SolarSystemHud.FormatDuration(plan.FlightTimeSeconds)));
            rows.Add(DetailRow.Of("Peak thrust",
                $"{plan.PeakThrustMps2:0.00} m/s²  ({plan.PeakThrottle * 100.0:0}%)"));
            rows.Add(DetailRow.Of("Delta-v", $"{plan.DeltaVMps / 1000.0:N0} km/s"));

            if (session.Plan == null)
            {
                rows.Add(DetailRow.Heading("PREVIEW"));
                rows.Add(DetailRow.Of("Status",
                    PreviewStatusLabel(session, currentJulianDate)));

                double staleSeconds = session.PreviewStaleInSeconds(currentJulianDate);
                rows.Add(DetailRow.Of("Plotted",
                    double.IsNaN(staleSeconds) ? "—" : FormatCountdown(staleSeconds)));

                if (session.IsPreviewStale(currentJulianDate))
                    rows.Add(DetailRow.Of("Note", "Preview stale · replot or begin anyway"));

                if (!string.IsNullOrEmpty(warpLabel))
                    rows.Add(DetailRow.Of("Warp", warpLabel));

                return rows;
            }

            rows.Add(DetailRow.Heading("NOW"));
            rows.Add(DetailRow.Of("Status", StatusLabel(session)));

            if (session.Status == FlightStatus.Flying || session.Status == FlightStatus.Arrived)
            {
                rows.Add(DetailRow.Of("Phase",
                    session.Phase.HasValue ? PhaseLabel(session.Phase.Value) : "—"));
                rows.Add(DetailRow.Of("Next event", NextEvent(session)));

                if (session.Status == FlightStatus.Flying)
                {
                    rows.Add(DetailRow.Of("Throttle",
                        $"{session.Throttle * 100.0:0}%  ·  " +
                        $"{session.CurrentThrust.MagnitudeMps2:0.00} m/s²"));
                }

                rows.Add(DetailRow.Of("Flip", FlipLabel(session)));
            }

            if (!string.IsNullOrEmpty(warpLabel))
                rows.Add(DetailRow.Of("Warp", warpLabel));

            if (!string.IsNullOrEmpty(session.Failure))
                rows.Add(DetailRow.Of("Note", session.Failure));

            return rows;
        }

        public static string PhaseLabel(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.Departure: return "Departure";
                case FlightPhase.Cruise: return "Cruise";
                case FlightPhase.Capture: return "Capture";
                default: return phase.ToString();
            }
        }

        public static string StatusLabel(FlightSession session) =>
            session == null ? "—" : StatusLabel(session, double.NaN);

        /// <summary>The status shown in the panel header. A preview has its own
        /// label and can be flagged stale once the clock has moved.</summary>
        public static string StatusLabel(FlightSession session, double currentJulianDate)
        {
            if (session == null) return "—";
            if (session.Plan != null) return StatusLabel(session.Status);
            return PreviewStatusLabel(session, currentJulianDate);
        }

        public static string PreviewStatusLabel(FlightSession session, double currentJulianDate)
        {
            if (session == null || session.Preview == null) return "Idle";
            return session.IsPreviewStale(currentJulianDate) ? "Preview, stale" : "Preview";
        }

        /// <summary>The countdown a preview shows in the panel header.</summary>
        public static string PreviewNextEvent(FlightSession session, double currentJulianDate)
        {
            if (session?.Preview == null) return "—";

            double seconds = (session.Preview.ArrivalJulianDate - currentJulianDate)
                             * JulianDate.SecondsPerDay;
            return seconds <= 0.0 ? "Arrival due" : "Arrival  " + FormatCountdown(seconds);
        }

        public static string StatusLabel(FlightStatus status)
        {
            switch (status)
            {
                case FlightStatus.Idle: return "Idle";
                case FlightStatus.Flying: return "Under way";
                case FlightStatus.Arrived: return "Arrived, in orbit";
                case FlightStatus.Aborted: return "Aborted";
                case FlightStatus.Rejected: return "Plan rejected";
                default: return status.ToString();
            }
        }

        /// <summary>`CAPTURE  T− 18h 12m`, or `Arriving` when the next boundary
        /// is the end of the plan.</summary>
        public static string NextEvent(FlightSession session)
        {
            if (session == null || session.Plan == null) return "—";

            double seconds = session.SecondsToNextEvent;
            if (!double.IsFinite(seconds)) return "Arrived";

            string phase = session.NextPhase.HasValue
                ? PhaseLabel(session.NextPhase.Value)
                : "Arrival";
            return $"{phase}  {FormatCountdown(seconds)}";
        }

        public static string FlipLabel(FlightSession session)
        {
            if (session == null || double.IsNaN(session.FlipJulianDate)) return "—";

            double seconds = (session.FlipJulianDate - session.State.JulianDate)
                             * JulianDate.SecondsPerDay;
            if (session.Status == FlightStatus.Arrived) return "passed";
            return seconds < 0.0 ? "passed" : FormatCountdown(seconds);
        }

        /// <summary>
        /// A countdown a person can read at a glance: days for long legs,
        /// hours and minutes near an arrival, seconds in the last minute.
        /// </summary>
        public static string FormatCountdown(double seconds)
        {
            if (double.IsNaN(seconds)) return "—";
            if (double.IsInfinity(seconds)) return seconds > 0 ? "—" : "now";

            string sign = seconds < 0.0 ? "T+ " : "T− ";
            seconds = Math.Abs(seconds);

            if (seconds >= JulianDate.SecondsPerDay)
                return $"{sign}{seconds / JulianDate.SecondsPerDay:0.00} d";
            if (seconds >= 3600.0)
            {
                int hours = (int)(seconds / 3600.0);
                int minutes = (int)(seconds % 3600.0 / 60.0);
                return $"{sign}{hours}h {minutes:00}m";
            }
            if (seconds >= 60.0)
            {
                int minutes = (int)(seconds / 60.0);
                int remainder = (int)(seconds % 60.0);
                return $"{sign}{minutes}m {remainder:00}s";
            }
            return $"{sign}{seconds:0} s";
        }
    }
}
