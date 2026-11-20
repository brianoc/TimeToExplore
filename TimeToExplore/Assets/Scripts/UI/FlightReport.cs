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
                    $"{SolarSystemHud.FormatDuration(arc.DurationSeconds)}  ·  " +
                    DriveLabel(arc, model)));
            }

            rows.Add(DetailRow.Of("Arrival", JulianDate.FormatShort(plan.ArrivalJulianDate)));
            rows.Add(DetailRow.Of("Flight time", SolarSystemHud.FormatDuration(plan.FlightTimeSeconds)));

            // A coasting profile has no torch plan: the peak-throttle row would
            // describe the capture's impulses as if they were a continuous
            // burn. Report what the drive actually does on each leg instead.
            bool coastProfile = IsCoastProfile(plan);
            if (coastProfile)
            {
                AddDriveRows(rows, plan, model);
            }
            else
            {
                rows.Add(DetailRow.Of("Peak thrust",
                    $"{plan.PeakThrustMps2:0.00} m/s²  ({plan.PeakThrottle * 100.0:0}%)"));
            }

            rows.Add(DetailRow.Of("Delta-v", $"{plan.DeltaVMps / 1000.0:N0} km/s"));

            if (!coastProfile)
            {
                if (plan.FiniteBurnDeltaVMps > 0.0 && plan.ImpulseDeltaVMps > 0.0)
                {
                    double g = plan.FiniteBurnAccelerationMps2 / TripPlanner.StandardGravityMps2;
                    rows.Add(DetailRow.Of("Burns",
                        $"{plan.FiniteBurnDeltaVMps / 1000.0:F1} km/s fixed  ·  " +
                        $"{plan.ImpulseDeltaVMps / 1000.0:F1} km/s trim  ·  {g:0.0} g"));
                }
                else if (plan.ImpulseDeltaVMps > 0.0)
                {
                    rows.Add(DetailRow.Of("Burns",
                        $"{plan.ImpulseDeltaVMps / 1000.0:F1} km/s  ·  impulsive"));
                }

                if (plan.FiniteBurnDeltaVMps > 0.0 && plan.ImpulseDeltaVMps <= 0.0)
                {
                    double g = plan.FiniteBurnAccelerationMps2 / TripPlanner.StandardGravityMps2;
                    rows.Add(DetailRow.Of("Burns",
                        $"{plan.FiniteBurnDeltaVMps / 1000.0:F1} km/s  ·  {g:0.0} g fixed"));
                }
            }

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
                        session.Throttle > 0.005
                            ? $"{session.Throttle * 100.0:0}%  ·  " +
                              $"{session.CurrentThrust.MagnitudeMps2:0.00} m/s²"
                            : "drive off"));
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
                case FlightPhase.Approach: return "Approach";
                case FlightPhase.Coast: return "Coast";
                case FlightPhase.Capture: return "Capture";
                case FlightPhase.Parking: return "Parking";
                default: return phase.ToString();
            }
        }

        /// <summary>What the drive is doing on one arc. The same path type reads
        /// differently by phase: a fixed burn on the cruise is the profile's
        /// departure burn, the one at the target's sphere of influence is the
        /// brake, and the hybrid's is the capture burn.</summary>
        public static string DriveLabel(PlanArc arc, SolarSystemModel model = null)
        {
            if (arc == null) return "—";

            bool aboutSun = model?.Sun != null && arc.ReferenceBodyId == model.Sun.Id;
            if (arc.IsFiniteBurn)
            {
                if (aboutSun && arc.Phase == FlightPhase.Cruise) return "prograde burn";
                if (aboutSun && arc.Phase == FlightPhase.Capture) return "brake";
                return "capture burn";
            }

            if (arc.IsCoast) return "drive off";
            return arc.Phase == FlightPhase.Departure ? "break" : "powered";
        }

        /// <summary>True when the plan crosses on a coast rather than a powered
        /// quintic: the coast cruise, and the burn–coast–burn profile whose
        /// heliocentric middle is the same coast.</summary>
        public static bool IsCoastProfile(TrajectoryPlan plan)
        {
            if (plan == null) return false;

            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase != FlightPhase.Cruise) continue;
                return arc.IsCoast || arc.IsFiniteBurn;
            }

            return false;
        }

        /// <summary>
        /// The drive summary for a coasting plan: the break out of the departure
        /// well, the coast with the drive off, the fixed departure burn and the
        /// brake reported separately with their own delta-v and durations, and
        /// the capture chain's own cost. The brake's direction is the change the
        /// chain needs, not a velocity-aligned retrograde burn, so the row says
        /// what it spends and how long it takes, not that it is anti-velocity.
        /// </summary>
        private static void AddDriveRows(List<DetailRow> rows, TrajectoryPlan plan,
            SolarSystemModel model)
        {
            rows.Add(DetailRow.Heading("DRIVE"));

            string sunId = model?.Sun?.Id;
            PlanArc departureBurn = null;
            PlanArc brake = null;
            PlanArc coast = null;
            PlanArc approach = null;
            int chainStart = plan.Arcs.Count;

            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                PlanArc arc = plan.Arcs[i];
                bool aboutSun = sunId != null && arc.ReferenceBodyId == sunId;

                if (aboutSun && arc.Phase == FlightPhase.Cruise && arc.IsFiniteBurn)
                    departureBurn = arc;
                else if (aboutSun && arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn)
                    brake = arc;
                else if (aboutSun && arc.Phase == FlightPhase.Cruise && arc.IsCoast)
                    coast = arc;
                else if (arc.Phase == FlightPhase.Approach)
                    approach = arc;

                if (chainStart == plan.Arcs.Count && !aboutSun &&
                    (arc.Phase == FlightPhase.Coast || arc.Phase == FlightPhase.Capture ||
                     arc.Phase == FlightPhase.Parking))
                    chainStart = i;
            }

            double level = plan.FiniteBurnAccelerationMps2
                           / TripPlanner.StandardGravityMps2;

            if (departureBurn != null)
            {
                rows.Add(DetailRow.Of("Departure burn",
                    $"{departureBurn.DeltaVMps / 1000.0:F1} km/s  ·  " +
                    $"{SolarSystemHud.FormatDuration(departureBurn.DurationSeconds)}  ·  " +
                    $"{level:0.0} g"));
            }
            else if (plan.Arcs.Count > 0 && plan.Arcs[0].Phase == FlightPhase.Departure)
            {
                PlanArc breakArc = plan.Arcs[0];
                rows.Add(DetailRow.Of("Break",
                    $"{breakArc.DeltaVMps / 1000.0:F1} km/s  ·  " +
                    $"{SolarSystemHud.FormatDuration(breakArc.DurationSeconds)}  ·  quintic"));
            }

            if (coast != null)
            {
                rows.Add(DetailRow.Of("Coast",
                    $"drive off  ·  {SolarSystemHud.FormatDuration(coast.DurationSeconds)}"));
            }

            if (approach != null)
            {
                rows.Add(DetailRow.Of("Approach",
                    $"drive off  ·  {SolarSystemHud.FormatDuration(approach.DurationSeconds)}"));
            }

            if (brake != null)
            {
                rows.Add(DetailRow.Of("Brake",
                    $"{brake.DeltaVMps / 1000.0:F1} km/s  ·  " +
                    $"{SolarSystemHud.FormatDuration(brake.DurationSeconds)}  ·  " +
                    $"{level:0.0} g"));
            }

            if (chainStart < plan.Arcs.Count)
            {
                double chainDeltaV = 0.0;
                if (brake == null && plan.Arcs[chainStart].EntryImpulseMps > 0.0)
                {
                    rows.Add(DetailRow.Of("Handover",
                        $"{plan.Arcs[chainStart].EntryImpulseMps / 1000.0:F1} km/s  ·  " +
                        "at the target SOI"));
                }

                for (int i = chainStart; i < plan.Arcs.Count; i++)
                {
                    chainDeltaV += plan.Arcs[i].EntryImpulseMps;
                    if (plan.Arcs[i].IsFiniteBurn) chainDeltaV += plan.Arcs[i].DeltaVMps;
                }

                double chainSeconds = (plan.ArrivalJulianDate
                                       - plan.Arcs[chainStart].StartJulianDate)
                                      * JulianDate.SecondsPerDay;
                rows.Add(DetailRow.Of("Capture",
                    $"{chainDeltaV / 1000.0:F1} km/s  ·  " +
                    SolarSystemHud.FormatDuration(chainSeconds)));
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
